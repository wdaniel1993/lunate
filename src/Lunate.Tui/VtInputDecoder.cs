using System.Buffers;
using System.Text;

namespace Lunate.Tui;

internal static class VtInputDecoder
{
    public static bool TryDecode(ReadOnlySpan<byte> input, out KeyEvent? key, out int consumed)
    {
        key = null;
        consumed = 0;
        if (input.IsEmpty)
        {
            return false;
        }

        if (input[0] == 0x1B)
        {
            if (input.Length == 1)
            {
                key = new KeyEvent(KeyKind.Escape, null, false, false, false);
                consumed = 1;
                return true;
            }

            return input[1] switch
            {
                (byte)'[' => TryDecodeCsi(input, out key, out consumed),
                (byte)'O' => TryDecodeSs3(input, out key, out consumed),
                _ => TryDecodeAlt(input, out key, out consumed),
            };
        }

        var status = DecodeUtf8(input, out var rune, out int length);
        if (status == OperationStatus.NeedMoreData)
        {
            return false;
        }

        if (status == OperationStatus.InvalidData)
        {
            key = new KeyEvent(KeyKind.Character, "\uFFFD", false, false, false);
            consumed = 1;
            return true;
        }

        consumed = length;
        key =
            rune.Value == 0x7F ? new KeyEvent(KeyKind.Backspace, null, false, false, false)
            : rune.Value < 0x20 ? DecodeControl((byte)rune.Value)
            : new KeyEvent(KeyKind.Character, rune.ToString(), false, false, false);
        return true;
    }

    private static bool TryDecodeAlt(ReadOnlySpan<byte> input, out KeyEvent? key, out int consumed)
    {
        key = null;
        consumed = 0;
        if (input[1] == 0x1B)
        {
            key = new KeyEvent(KeyKind.Escape, null, false, false, true);
            consumed = 2;
            return true;
        }

        var status = DecodeUtf8(input[1..], out var rune, out int length);
        if (status == OperationStatus.NeedMoreData)
        {
            return false;
        }

        if (status == OperationStatus.InvalidData)
        {
            key = new KeyEvent(KeyKind.Unknown, null, false, false, false);
            consumed = 2;
            return true;
        }

        consumed = 1 + length;
        key =
            rune.Value == 0x7F ? new KeyEvent(KeyKind.Backspace, null, false, false, true)
            : rune.Value < 0x20 ? DecodeControl((byte)rune.Value) with { Alt = true }
            : new KeyEvent(KeyKind.Character, rune.ToString(), false, false, true);
        return true;
    }

    private static KeyEvent DecodeControl(byte value) =>
        value switch
        {
            0x00 => new KeyEvent(KeyKind.Space, null, true, false, false),
            0x09 => new KeyEvent(KeyKind.Tab, null, false, false, false),
            0x0D => new KeyEvent(KeyKind.Enter, null, false, false, false),
            0x1C => new KeyEvent(KeyKind.Character, "\\", true, false, false),
            0x1D => new KeyEvent(KeyKind.Character, "]", true, false, false),
            0x1E => new KeyEvent(KeyKind.Character, "^", true, false, false),
            0x1F => new KeyEvent(KeyKind.Character, "_", true, false, false),
            <= 0x1A => new KeyEvent(
                KeyKind.Character,
                ((char)(value + 0x60)).ToString(),
                true,
                false,
                false
            ),
            _ => new KeyEvent(KeyKind.Unknown, null, true, false, false),
        };

    private static bool TryDecodeCsi(ReadOnlySpan<byte> input, out KeyEvent? key, out int consumed)
    {
        key = null;
        consumed = 0;
        var i = 2;
        while (i < input.Length && input[i] is >= 0x20 and <= 0x3F)
        {
            i++;
        }

        if (i >= input.Length)
        {
            return false;
        }

        if (input[i] is < 0x40 or > 0x7E)
        {
            key = new KeyEvent(KeyKind.Unknown, null, false, false, false);
            consumed = 2;
            return true;
        }

        var final = input[i];
        var parameters = ParseParameters(input[2..i]);
        var (shift, alt, ctrl) = (false, false, false);
        KeyKind kind = final switch
        {
            (byte)'A' => KeyKind.Up,
            (byte)'B' => KeyKind.Down,
            (byte)'C' => KeyKind.Right,
            (byte)'D' => KeyKind.Left,
            (byte)'H' => KeyKind.Home,
            (byte)'F' => KeyKind.End,
            (byte)'Z' => KeyKind.Tab,
            (byte)'~' => TildeKind(parameters),
            _ => KeyKind.Unknown,
        };

        if (final == (byte)'Z')
        {
            shift = true;
        }
        else if (parameters.Length >= 2)
        {
            (shift, alt, ctrl) = Modifiers(parameters[1]);
        }

        key = new KeyEvent(kind, null, ctrl, shift, alt);
        consumed = i + 1;
        return true;
    }

    private static bool TryDecodeSs3(ReadOnlySpan<byte> input, out KeyEvent? key, out int consumed)
    {
        key = null;
        consumed = 0;
        if (input.Length < 3)
        {
            return false;
        }

        KeyKind kind = input[2] switch
        {
            (byte)'A' => KeyKind.Up,
            (byte)'B' => KeyKind.Down,
            (byte)'C' => KeyKind.Right,
            (byte)'D' => KeyKind.Left,
            (byte)'H' => KeyKind.Home,
            (byte)'F' => KeyKind.End,
            (byte)'P' => KeyKind.F1,
            (byte)'Q' => KeyKind.F2,
            (byte)'R' => KeyKind.F3,
            (byte)'S' => KeyKind.F4,
            _ => KeyKind.Unknown,
        };

        key = new KeyEvent(kind, null, false, false, false);
        consumed = 3;
        return true;
    }

    private static KeyKind TildeKind(int[] parameters) =>
        parameters.Length > 0
            ? parameters[0] switch
            {
                1 or 7 => KeyKind.Home,
                2 => KeyKind.Insert,
                3 => KeyKind.Delete,
                4 or 8 => KeyKind.End,
                5 => KeyKind.PageUp,
                6 => KeyKind.PageDown,
                11 => KeyKind.F1,
                12 => KeyKind.F2,
                13 => KeyKind.F3,
                14 => KeyKind.F4,
                15 => KeyKind.F5,
                17 => KeyKind.F6,
                18 => KeyKind.F7,
                19 => KeyKind.F8,
                20 => KeyKind.F9,
                21 => KeyKind.F10,
                23 => KeyKind.F11,
                24 => KeyKind.F12,
                _ => KeyKind.Unknown,
            }
            : KeyKind.Unknown;

    private static (bool Shift, bool Alt, bool Ctrl) Modifiers(int value)
    {
        var bits = value - 1;
        return ((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0);
    }

    private static int[] ParseParameters(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty)
        {
            return [];
        }

        var parts = new List<int>();
        var value = 0;
        var has = false;
        foreach (var b in span)
        {
            if (b == (byte)';')
            {
                parts.Add(has ? value : 0);
                value = 0;
                has = false;
            }
            else if (b is >= (byte)'0' and <= (byte)'9')
            {
                value = value * 10 + (b - (byte)'0');
                has = true;
            }
        }

        parts.Add(has ? value : 0);
        return [.. parts];
    }

    private static OperationStatus DecodeUtf8(
        ReadOnlySpan<byte> input,
        out Rune rune,
        out int consumed
    )
    {
        var status = Rune.DecodeFromUtf8(input, out rune, out consumed);
        if (status == OperationStatus.InvalidData)
        {
            rune = Rune.ReplacementChar;
            consumed = 1;
        }

        return status;
    }
}

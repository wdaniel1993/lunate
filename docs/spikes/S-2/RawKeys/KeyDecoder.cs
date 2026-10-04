using System.Buffers;
using System.Text;

namespace Spike.RawKeys;

public static class KeyDecoder
{
    public static bool TryDecode(ReadOnlySpan<byte> input, out KeyEvent? key, out int consumed)
    {
        key = null;
        consumed = 0;
        if (input.IsEmpty) return false;

        if (input[0] == 0x1B)
        {
            if (input.Length == 1)
            {
                key = new KeyEvent("Escape", null, false, false, false, input.ToArray());
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

        if (!TryDecodeUtf8(input, out var ch, out var n)) return false;

        if (n == 1 && ch < 0x20) return TryDecodeControl((byte)ch, out key, out consumed);

        if (n == 1 && ch == 0x7F)
        {
            key = new KeyEvent("Backspace", null, false, false, false, input[..1].ToArray());
            consumed = 1;
            return true;
        }

        key = new KeyEvent(NameFor(ch), ch, false, false, false, input[..n].ToArray());
        consumed = n;
        return true;
    }

    private static bool TryDecodeAlt(ReadOnlySpan<byte> input, out KeyEvent? key, out int consumed)
    {
        key = null;
        consumed = 0;
        if (TryDecodeUtf8(input[1..], out var ch, out var n))
        {
            key = new KeyEvent(NameFor(ch), ch, false, false, true, input[..(1 + n)].ToArray());
            consumed = 1 + n;
            return true;
        }

        key = new KeyEvent("Unknown", null, false, false, false, input[..2].ToArray());
        consumed = 2;
        return true;
    }

    private static bool TryDecodeControl(byte b, out KeyEvent? key, out int consumed)
    {
        consumed = 1;
        key = b switch
        {
            0x00 => new KeyEvent("Space", null, true, false, false, [b]),
            0x09 => new KeyEvent("Tab", null, false, false, false, [b]),
            0x0D => new KeyEvent("Enter", null, false, false, false, [b]),
            <= 0x1A => new KeyEvent(((char)(b + 0x40)).ToString(), null, true, false, false, [b]),
            0x1C or 0x1D or 0x1E or 0x1F => new KeyEvent(((char)(b + 0x40)).ToString(), null, true, false, false, [b]),
            _ => new KeyEvent("Unknown", null, true, false, false, [b]),
        };
        return true;
    }

    private static bool TryDecodeCsi(ReadOnlySpan<byte> input, out KeyEvent? key, out int consumed)
    {
        key = null;
        consumed = 0;
        var i = 2;
        while (i < input.Length && input[i] is >= 0x20 and <= 0x3F) i++;
        if (i >= input.Length) return false;
        if (input[i] is < 0x40 or > 0x7E)
        {
            key = new KeyEvent("Unknown", null, false, false, false, input[..2].ToArray());
            consumed = 2;
            return true;
        }

        var final = input[i];
        var parameters = ParseParameters(input[2..i]);
        var raw = input[..(i + 1)].ToArray();
        var (shift, alt, ctrl) = (false, false, false);

        string? name = final switch
        {
            (byte)'A' => "Up",
            (byte)'B' => "Down",
            (byte)'C' => "Right",
            (byte)'D' => "Left",
            (byte)'H' => "Home",
            (byte)'F' => "End",
            (byte)'Z' => "Tab",
            (byte)'~' => parameters.Length > 0
                ? parameters[0] switch
                {
                    1 or 7 => "Home",
                    2 => "Insert",
                    3 => "Delete",
                    4 or 8 => "End",
                    5 => "PageUp",
                    6 => "PageDown",
                    >= 11 and <= 24 => FKeyName(parameters[0]),
                    _ => null,
                }
                : null,
            _ => null,
        };

        if (final == (byte)'Z')
        {
            shift = true;
        }
        else if (parameters.Length >= 2)
        {
            (shift, alt, ctrl) = Modifiers(parameters[1]);
        }

        key = new KeyEvent(name ?? "Unknown", null, ctrl, shift, alt, raw);
        consumed = raw.Length;
        return true;
    }

    private static bool TryDecodeSs3(ReadOnlySpan<byte> input, out KeyEvent? key, out int consumed)
    {
        key = null;
        consumed = 0;
        if (input.Length < 3) return false;
        var name = input[2] switch
        {
            (byte)'A' => "Up",
            (byte)'B' => "Down",
            (byte)'C' => "Right",
            (byte)'D' => "Left",
            (byte)'H' => "Home",
            (byte)'F' => "End",
            (byte)'P' => "F1",
            (byte)'Q' => "F2",
            (byte)'R' => "F3",
            (byte)'S' => "F4",
            _ => "Unknown",
        };
        key = new KeyEvent(name, null, false, false, false, input[..3].ToArray());
        consumed = 3;
        return true;
    }

    private static string? FKeyName(int code) => code switch
    {
        11 => "F1",
        12 => "F2",
        13 => "F3",
        14 => "F4",
        15 => "F5",
        17 => "F6",
        18 => "F7",
        19 => "F8",
        20 => "F9",
        21 => "F10",
        23 => "F11",
        24 => "F12",
        _ => null,
    };

    private static (bool Shift, bool Alt, bool Ctrl) Modifiers(int value)
    {
        var bits = value - 1;
        return ((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0);
    }

    private static int[] ParseParameters(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty) return [];
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

    private static bool TryDecodeUtf8(ReadOnlySpan<byte> input, out char ch, out int consumed)
    {
        ch = default;
        consumed = 0;
        if (input.IsEmpty) return false;
        var status = Rune.DecodeFromUtf8(input, out var rune, out var n);
        if (status == OperationStatus.NeedMoreData) return false;
        consumed = status == OperationStatus.Done ? n : 1;
        ch = status == OperationStatus.Done && rune.IsBmp ? (char)rune.Value : '\uFFFD';
        return true;
    }

    private static string NameFor(char ch) => ch switch
    {
        ' ' => "Space",
        _ when char.IsAscii(ch) => char.ToUpperInvariant(ch).ToString(),
        _ => "Unicode",
    };
}

using System.Text;

namespace Lunate.Tui.Tests;

public sealed class VtInputDecoderTests
{
    [Theory]
    [MemberData(nameof(SpikeCases))]
    public void Ported_spike_cases_decode_to_the_expected_events(
        string name,
        byte[] input,
        KeyEvent? expected
    )
    {
        bool decoded = VtInputDecoder.TryDecode(input, out var key, out int consumed);

        if (expected is null)
        {
            Assert.False(decoded, name);
            Assert.Equal(0, consumed);
            return;
        }

        Assert.True(decoded, name);
        Assert.Equal(expected, key);
        Assert.Equal(input.Length, consumed);
    }

    [Theory]
    [InlineData(0x0A, "j")]
    [InlineData(0x01, "a")]
    [InlineData(0x1A, "z")]
    public void Control_letters_map_to_lowercase_text_with_ctrl(int control, string letter)
    {
        bool decoded = VtInputDecoder.TryDecode([(byte)control], out var key, out _);

        Assert.True(decoded);
        Assert.Equal(new KeyEvent(KeyKind.Character, letter, true, false, false), key);
    }

    [Fact]
    public void Xterm_tilde_numbering_is_not_linear()
    {
        Assert.Equal(KeyKind.F1, Decode("11~").Kind);
        Assert.Equal(KeyKind.F10, Decode("21~").Kind);
        Assert.Equal(KeyKind.F11, Decode("23~").Kind);
        Assert.Equal(KeyKind.F12, Decode("24~").Kind);
        Assert.Equal(KeyKind.Unknown, Decode("16~").Kind);
        Assert.Equal(KeyKind.Unknown, Decode("22~").Kind);
    }

    [Fact]
    public void Tilde_home_and_end_variants_decode()
    {
        Assert.Equal(KeyKind.Home, Decode("1~").Kind);
        Assert.Equal(KeyKind.Home, Decode("7~").Kind);
        Assert.Equal(KeyKind.End, Decode("4~").Kind);
        Assert.Equal(KeyKind.End, Decode("8~").Kind);
    }

    [Fact]
    public void Ss3_arrows_decode()
    {
        Assert.Equal(KeyKind.Up, DecodeSs3('A').Kind);
        Assert.Equal(KeyKind.Down, DecodeSs3('B').Kind);
        Assert.Equal(KeyKind.Right, DecodeSs3('C').Kind);
        Assert.Equal(KeyKind.Left, DecodeSs3('D').Kind);
    }

    [Fact]
    public void A_four_byte_emoji_decodes_as_character_text()
    {
        bool decoded = VtInputDecoder.TryDecode(
            [0xF0, 0x9F, 0x99, 0x82],
            out var key,
            out int consumed
        );

        Assert.True(decoded);
        Assert.Equal(new KeyEvent(KeyKind.Character, "🙂", false, false, false), key);
        Assert.Equal(4, consumed);
    }

    [Fact]
    public void Invalid_utf8_becomes_a_replacement_character()
    {
        bool decoded = VtInputDecoder.TryDecode([0x80], out var key, out int consumed);

        Assert.True(decoded);
        Assert.Equal(new KeyEvent(KeyKind.Character, "\uFFFD", false, false, false), key);
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void Escape_escape_is_alt_escape()
    {
        bool decoded = VtInputDecoder.TryDecode([0x1B, 0x1B], out var key, out int consumed);

        Assert.True(decoded);
        Assert.Equal(new KeyEvent(KeyKind.Escape, null, false, false, true), key);
        Assert.Equal(2, consumed);
    }

    [Fact]
    public void Alt_control_keeps_the_control_mapping_and_adds_alt()
    {
        bool decoded = VtInputDecoder.TryDecode([0x1B, 0x03], out var key, out _);

        Assert.True(decoded);
        Assert.Equal(new KeyEvent(KeyKind.Character, "c", true, false, true), key);
    }

    [Fact]
    public void Empty_input_is_not_decodable()
    {
        Assert.False(VtInputDecoder.TryDecode([], out _, out int consumed));
        Assert.Equal(0, consumed);
    }

    private static KeyEvent Decode(string sequence)
    {
        byte[] input = Encoding.ASCII.GetBytes("\u001b[" + sequence);
        bool decoded = VtInputDecoder.TryDecode(input, out var key, out _);
        Assert.True(decoded);
        return Assert.IsType<KeyEvent>(key);
    }

    private static KeyEvent DecodeSs3(char final)
    {
        bool decoded = VtInputDecoder.TryDecode([0x1B, (byte)'O', (byte)final], out var key, out _);
        Assert.True(decoded);
        return Assert.IsType<KeyEvent>(key);
    }

    public static TheoryData<string, byte[], KeyEvent?> SpikeCases() =>
        new()
        {
            { "printable-a", B(0x61), new KeyEvent(KeyKind.Character, "a", false, false, false) },
            { "shifted-A", B(0x41), new KeyEvent(KeyKind.Character, "A", false, false, false) },
            { "enter", B(0x0D), new KeyEvent(KeyKind.Enter, null, false, false, false) },
            { "tab", B(0x09), new KeyEvent(KeyKind.Tab, null, false, false, false) },
            { "backspace", B(0x7F), new KeyEvent(KeyKind.Backspace, null, false, false, false) },
            { "ctrl-c", B(0x03), new KeyEvent(KeyKind.Character, "c", true, false, false) },
            { "ctrl-space", B(0x00), new KeyEvent(KeyKind.Space, null, true, false, false) },
            { "escape", B(0x1B), new KeyEvent(KeyKind.Escape, null, false, false, false) },
            { "alt-x", B(0x1B, 0x78), new KeyEvent(KeyKind.Character, "x", false, false, true) },
            { "up", B(0x1B, 0x5B, 0x41), new KeyEvent(KeyKind.Up, null, false, false, false) },
            { "down", B(0x1B, 0x5B, 0x42), new KeyEvent(KeyKind.Down, null, false, false, false) },
            {
                "right",
                B(0x1B, 0x5B, 0x43),
                new KeyEvent(KeyKind.Right, null, false, false, false)
            },
            { "left", B(0x1B, 0x5B, 0x44), new KeyEvent(KeyKind.Left, null, false, false, false) },
            {
                "home-csi",
                B(0x1B, 0x5B, 0x48),
                new KeyEvent(KeyKind.Home, null, false, false, false)
            },
            {
                "end-csi",
                B(0x1B, 0x5B, 0x46),
                new KeyEvent(KeyKind.End, null, false, false, false)
            },
            {
                "insert",
                B(0x1B, 0x5B, 0x32, 0x7E),
                new KeyEvent(KeyKind.Insert, null, false, false, false)
            },
            {
                "delete",
                B(0x1B, 0x5B, 0x33, 0x7E),
                new KeyEvent(KeyKind.Delete, null, false, false, false)
            },
            {
                "page-up",
                B(0x1B, 0x5B, 0x35, 0x7E),
                new KeyEvent(KeyKind.PageUp, null, false, false, false)
            },
            {
                "page-down",
                B(0x1B, 0x5B, 0x36, 0x7E),
                new KeyEvent(KeyKind.PageDown, null, false, false, false)
            },
            { "f1-ss3", B(0x1B, 0x4F, 0x50), new KeyEvent(KeyKind.F1, null, false, false, false) },
            { "f4-ss3", B(0x1B, 0x4F, 0x53), new KeyEvent(KeyKind.F4, null, false, false, false) },
            {
                "f5-csi",
                B(0x1B, 0x5B, 0x31, 0x35, 0x7E),
                new KeyEvent(KeyKind.F5, null, false, false, false)
            },
            {
                "f12-csi",
                B(0x1B, 0x5B, 0x32, 0x34, 0x7E),
                new KeyEvent(KeyKind.F12, null, false, false, false)
            },
            {
                "shift-tab",
                B(0x1B, 0x5B, 0x5A),
                new KeyEvent(KeyKind.Tab, null, false, true, false)
            },
            {
                "ctrl-right",
                B(0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x43),
                new KeyEvent(KeyKind.Right, null, true, false, false)
            },
            {
                "shift-up",
                B(0x1B, 0x5B, 0x31, 0x3B, 0x32, 0x41),
                new KeyEvent(KeyKind.Up, null, false, true, false)
            },
            {
                "alt-f5",
                B(0x1B, 0x5B, 0x31, 0x35, 0x3B, 0x33, 0x7E),
                new KeyEvent(KeyKind.F5, null, false, false, true)
            },
            {
                "utf8-a-umlaut",
                B(0xC3, 0xA4),
                new KeyEvent(KeyKind.Character, "ä", false, false, false)
            },
            {
                "unknown-csi",
                B(0x1B, 0x5B, 0x39, 0x39, 0x7E),
                new KeyEvent(KeyKind.Unknown, null, false, false, false)
            },
            { "csi-partial-esc-bracket", B(0x1B, 0x5B), null },
            { "csi-partial-f5-prefix", B(0x1B, 0x5B, 0x31, 0x35), null },
            { "csi-partial-params", B(0x1B, 0x5B, 0x31, 0x3B), null },
        };

    private static byte[] B(params int[] bytes) => bytes.Select(b => (byte)b).ToArray();
}

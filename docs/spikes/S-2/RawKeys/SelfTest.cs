namespace Spike.RawKeys;

internal static class SelfTest
{
    private sealed record Expect(string Name, char? Char, bool Ctrl, bool Shift, bool Alt);

    public static int Run()
    {
        var cases = Cases();
        var passed = 0;
        foreach (var (name, input, expected) in cases)
        {
            var ok = KeyDecoder.TryDecode(input, out var key, out var consumed);
            bool good;
            string actual;
            if (expected is null)
            {
                good = !ok && consumed == 0;
                actual = ok ? $"decoded {key!.Display()}" : $"incomplete consumed={consumed}";
            }
            else
            {
                good =
                    ok
                    && key is not null
                    && key.Name == expected.Name
                    && key.Char == expected.Char
                    && key.Ctrl == expected.Ctrl
                    && key.Shift == expected.Shift
                    && key.Alt == expected.Alt
                    && consumed == input.Length;
                actual = ok && key is not null ? key.Display() : "incomplete";
            }
            Console.WriteLine($"{(good ? "PASS" : "FAIL")} {name}: {actual}");
            if (good)
                passed++;
        }
        Console.WriteLine($"self-test: {passed}/{cases.Count} passed");
        return passed == cases.Count ? 0 : 1;
    }

    private static List<(string Name, byte[] Input, Expect? Expected)> Cases() =>
        [
            ("printable-a", B(0x61), new Expect("A", 'a', false, false, false)),
            ("shifted-A", B(0x41), new Expect("A", 'A', false, false, false)),
            ("enter", B(0x0D), new Expect("Enter", null, false, false, false)),
            ("tab", B(0x09), new Expect("Tab", null, false, false, false)),
            ("backspace", B(0x7F), new Expect("Backspace", null, false, false, false)),
            ("ctrl-c", B(0x03), new Expect("C", null, true, false, false)),
            ("ctrl-space", B(0x00), new Expect("Space", null, true, false, false)),
            ("escape", B(0x1B), new Expect("Escape", null, false, false, false)),
            ("alt-x", B(0x1B, 0x78), new Expect("X", 'x', false, false, true)),
            ("up", B(0x1B, 0x5B, 0x41), new Expect("Up", null, false, false, false)),
            ("down", B(0x1B, 0x5B, 0x42), new Expect("Down", null, false, false, false)),
            ("right", B(0x1B, 0x5B, 0x43), new Expect("Right", null, false, false, false)),
            ("left", B(0x1B, 0x5B, 0x44), new Expect("Left", null, false, false, false)),
            ("home-csi", B(0x1B, 0x5B, 0x48), new Expect("Home", null, false, false, false)),
            ("end-csi", B(0x1B, 0x5B, 0x46), new Expect("End", null, false, false, false)),
            ("insert", B(0x1B, 0x5B, 0x32, 0x7E), new Expect("Insert", null, false, false, false)),
            ("delete", B(0x1B, 0x5B, 0x33, 0x7E), new Expect("Delete", null, false, false, false)),
            ("page-up", B(0x1B, 0x5B, 0x35, 0x7E), new Expect("PageUp", null, false, false, false)),
            (
                "page-down",
                B(0x1B, 0x5B, 0x36, 0x7E),
                new Expect("PageDown", null, false, false, false)
            ),
            ("f1-ss3", B(0x1B, 0x4F, 0x50), new Expect("F1", null, false, false, false)),
            ("f4-ss3", B(0x1B, 0x4F, 0x53), new Expect("F4", null, false, false, false)),
            (
                "f5-csi",
                B(0x1B, 0x5B, 0x31, 0x35, 0x7E),
                new Expect("F5", null, false, false, false)
            ),
            (
                "f12-csi",
                B(0x1B, 0x5B, 0x32, 0x34, 0x7E),
                new Expect("F12", null, false, false, false)
            ),
            ("shift-tab", B(0x1B, 0x5B, 0x5A), new Expect("Tab", null, false, true, false)),
            (
                "ctrl-right",
                B(0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x43),
                new Expect("Right", null, true, false, false)
            ),
            (
                "shift-up",
                B(0x1B, 0x5B, 0x31, 0x3B, 0x32, 0x41),
                new Expect("Up", null, false, true, false)
            ),
            (
                "alt-f5",
                B(0x1B, 0x5B, 0x31, 0x35, 0x3B, 0x33, 0x7E),
                new Expect("F5", null, false, false, true)
            ),
            ("utf8-a-umlaut", B(0xC3, 0xA4), new Expect("Unicode", 'ä', false, false, false)),
            (
                "unknown-csi",
                B(0x1B, 0x5B, 0x39, 0x39, 0x7E),
                new Expect("Unknown", null, false, false, false)
            ),
            ("csi-partial-esc-bracket", B(0x1B, 0x5B), null),
            ("csi-partial-f5-prefix", B(0x1B, 0x5B, 0x31, 0x35), null),
            ("csi-partial-params", B(0x1B, 0x5B, 0x31, 0x3B), null),
        ];

    private static byte[] B(params int[] bytes) => bytes.Select(b => (byte)b).ToArray();
}

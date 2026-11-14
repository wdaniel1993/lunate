using System.Globalization;
using Spectre.Console;

namespace Lunate.Tui;

/// <summary>
/// One themed colour, in the form the theme declared it: an ANSI name, a 256-colour index, or an
/// RGB triple (from hex). The form is kept because a theme that names a colour should render that
/// name at every depth — the built-in themes reproduce the bytes the surfaces painted before the
/// tokens — while a hex value downgrades by capability: true colour as RGB, 256 as the nearest
/// xterm entry, sixteen as the nearest of the sixteen, and no colour at all under <c>NO_COLOR</c>.
/// </summary>
internal readonly record struct ThemeColor(
    byte Red,
    byte Green,
    byte Blue,
    byte? Sixteen = null,
    int? Index = null,
    bool IsNamed = false
)
{
    /// <summary>The sixteen ANSI colours as xterm's default palette gives them.</summary>
    private static readonly (string Name, byte Red, byte Green, byte Blue)[] Ansi16 =
    [
        ("black", 0, 0, 0),
        ("maroon", 128, 0, 0),
        ("green", 0, 128, 0),
        ("olive", 128, 128, 0),
        ("navy", 0, 0, 128),
        ("purple", 128, 0, 128),
        ("teal", 0, 128, 128),
        ("silver", 192, 192, 192),
        ("grey", 128, 128, 128),
        ("red", 255, 0, 0),
        ("lime", 0, 255, 0),
        ("yellow", 255, 255, 0),
        ("blue", 0, 0, 255),
        ("fuchsia", 255, 0, 255),
        ("aqua", 0, 255, 255),
        ("white", 255, 255, 255),
    ];

    /// <summary>The bright names a theme file may use for the second half of the sixteen.</summary>
    private static readonly Dictionary<string, int> BrightNames = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["bright-black"] = 8,
        ["bright-red"] = 9,
        ["bright-green"] = 10,
        ["bright-yellow"] = 11,
        ["bright-blue"] = 12,
        ["bright-magenta"] = 13,
        ["bright-cyan"] = 14,
        ["bright-white"] = 15,
    };

    /// <summary>The secondary names a theme file may use beside the canonical sixteen.</summary>
    private static readonly Dictionary<string, int> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gray"] = 8,
        ["magenta"] = 13,
        ["cyan"] = 14,
    };

    private static readonly int[] CubeLevels = [0, 95, 135, 175, 215, 255];

    private const string ResetSgr = "\u001b[0m";

    private const string Accepted =
        "an ANSI name like 'red' or 'bright-blue', '#rrggbb', or 'colour:208'";

    /// <summary>One of the sixteen, as a theme file names it; the name is what renders.</summary>
    internal static ThemeColor Named(int index)
    {
        (_, byte red, byte green, byte blue) = Ansi16[index];
        return new ThemeColor(red, green, blue, (byte)index, index, IsNamed: true);
    }

    /// <summary>
    /// Parses one colour value: an ANSI name (with a <c>bright-</c> prefix for the bright half), a
    /// hex triple, or <c>colour:&lt;0-255&gt;</c> for a 256-colour index. Anything else is refused
    /// with the accepted forms named.
    /// </summary>
    internal static bool TryParse(string value, out ThemeColor color, out string? error)
    {
        color = default;
        error = null;
        string text = value.Trim();
        if (text.Length == 0)
        {
            error = $"is empty; use {Accepted}";
            return false;
        }

        if (text.StartsWith('#'))
        {
            if (
                text.Length == 7
                && byte.TryParse(
                    text.AsSpan(1, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out byte red
                )
                && byte.TryParse(
                    text.AsSpan(3, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out byte green
                )
                && byte.TryParse(
                    text.AsSpan(5, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out byte blue
                )
            )
            {
                color = new ThemeColor(red, green, blue);
                return true;
            }

            error = $"'{value}' is not a '#rrggbb' hex colour";
            return false;
        }

        int separator = text.IndexOf(':');
        if (separator > 0)
        {
            string prefix = text[..separator].Trim();
            string number = text[(separator + 1)..].Trim();
            if (
                (
                    prefix.Equals("colour", StringComparison.OrdinalIgnoreCase)
                    || prefix.Equals("color", StringComparison.OrdinalIgnoreCase)
                )
                && int.TryParse(
                    number,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int index
                )
                && index is >= 0 and <= 255
            )
            {
                color = FromXterm(index);
                return true;
            }
        }

        if (TryName(text, out color))
        {
            return true;
        }

        error = $"'{value}' is not a colour; use {Accepted}";
        return false;
    }

    /// <summary>The colour as a Spectre markup attribute, or null when the session has no colour.</summary>
    internal string? MarkupColor(ColorSystemSupport color) =>
        color switch
        {
            ColorSystemSupport.NoColors => null,
            // A named colour renders as its name at every depth, so a terminal that knows the name
            // paints it the way it painted the palette's older, name-based spans.
            _ when IsNamed => Ansi16[Index ?? Sixteen ?? Nearest16()].Name,
            // A hex or 256-index value has no name to render: true colour and 256 get the RGB (a
            // 256 entry keeps its exact index), and the console downgrades it; every shallower
            // capability gets the token's pinned sixteen-colour fallback, so the scrollback and the
            // live frame agree on which colour a token is.
            ColorSystemSupport.TrueColor or ColorSystemSupport.EightBit => Hex(),
            _ => Ansi16[Sixteen ?? Nearest16()].Name,
        };

    /// <summary>
    /// The colour as the raw SGR the live frame composes around already-laid-out text, or null
    /// when the session has no colour. <c>Detect</c> resolves to the sixteen-colour set, matching
    /// what the frame painted before the tokens; the scrollback's console decides its own depth.
    /// </summary>
    internal string? Sgr(ColorSystemSupport color) =>
        color switch
        {
            ColorSystemSupport.NoColors => null,
            // A named colour keeps its exact entry under 256 colours and true colour — the byte
            // sequence the surfaces emitted before the tokens — and its 16-colour code otherwise.
            ColorSystemSupport.TrueColor or ColorSystemSupport.EightBit when IsNamed => Sgr256(
                Index ?? 0
            ),
            ColorSystemSupport.TrueColor or ColorSystemSupport.EightBit when Index is { } index =>
                Sgr256(index),
            ColorSystemSupport.TrueColor => string.Create(
                CultureInfo.InvariantCulture,
                $"\u001b[38;2;{Red};{Green};{Blue}m"
            ),
            ColorSystemSupport.EightBit => Sgr256(Nearest256()),
            _ => string.Create(CultureInfo.InvariantCulture, $"\u001b[{AnsiCode()}m"),
        };

    /// <summary>Wraps one span in this colour and the full reset; the text alone without colour.</summary>
    internal string Wrap(string text, ColorSystemSupport color)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        return Sgr(color) is { } sgr ? sgr + text + ResetSgr : text;
    }

    private static string Sgr256(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"\u001b[38;5;{index}m");

    private string Hex() =>
        string.Create(CultureInfo.InvariantCulture, $"#{Red:x2}{Green:x2}{Blue:x2}");

    /// <summary>The escape parameters of one of the sixteen: 30-37 for the base, 90-97 for bright.</summary>
    private int AnsiCode()
    {
        // Only a named colour has a fixed sixteen-colour entry; a 256-index value above the
        // sixteen has to be resolved to the nearest one first.
        int index = Sixteen ?? (IsNamed ? Index ?? Nearest16() : Nearest16());
        return index < 8 ? 30 + index : 90 + (index - 8);
    }

    private int Nearest16()
    {
        int best = 0;
        int distance = int.MaxValue;
        for (var index = 0; index < Ansi16.Length; index++)
        {
            (_, byte red, byte green, byte blue) = Ansi16[index];
            int candidate = Distance(red, green, blue);
            if (candidate < distance)
            {
                best = index;
                distance = candidate;
            }
        }

        return best;
    }

    private int Nearest256()
    {
        int best = 0;
        int distance = int.MaxValue;
        for (var index = 0; index < 256; index++)
        {
            (byte red, byte green, byte blue) = Xterm(index);
            int candidate = Distance(red, green, blue);
            if (candidate < distance)
            {
                best = index;
                distance = candidate;
            }
        }

        return best;
    }

    private int Distance(byte red, byte green, byte blue)
    {
        int dr = Red - red;
        int dg = Green - green;
        int db = Blue - blue;
        return (dr * dr) + (dg * dg) + (db * db);
    }

    /// <summary>The xterm entry for a 256-colour index: a system colour, the cube, or a grey ramp.</summary>
    private static ThemeColor FromXterm(int index)
    {
        if (index < 16)
        {
            return Named(index);
        }

        (byte red, byte green, byte blue) = Xterm(index);
        return new ThemeColor(red, green, blue, Index: index);
    }

    private static (byte, byte, byte) Xterm(int index)
    {
        if (index < 16)
        {
            (_, byte red, byte green, byte blue) = Ansi16[index];
            return (red, green, blue);
        }

        if (index < 232)
        {
            int cube = index - 16;
            return (
                (byte)CubeLevels[cube / 36],
                (byte)CubeLevels[cube / 6 % 6],
                (byte)CubeLevels[cube % 6]
            );
        }

        byte grey = (byte)(8 + (10 * (index - 232)));
        return (grey, grey, grey);
    }

    private static bool TryName(string text, out ThemeColor color)
    {
        color = default;
        if (BrightNames.TryGetValue(text, out int bright))
        {
            color = Named(bright);
            return true;
        }

        IEnumerable<(string Name, byte Red, byte Green, byte Blue, int Index)> entries =
            Ansi16.Select(
                (entry, index) => (entry.Name, entry.Red, entry.Green, entry.Blue, Index: index)
            );
        foreach (var entry in entries)
        {
            if (string.Equals(entry.Name, text, StringComparison.OrdinalIgnoreCase))
            {
                color = Named(entry.Index);
                return true;
            }
        }

        if (Aliases.TryGetValue(text, out int aliased))
        {
            color = Named(aliased);
            return true;
        }

        return false;
    }
}

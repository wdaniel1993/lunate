using System.Globalization;
using System.Text;

namespace Lunate.Tui;

internal static class CellText
{
    public static int Width(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += Width(rune);
        }

        return width;
    }

    internal static int Width(Rune rune)
    {
        if (Rune.IsControl(rune))
        {
            return 0;
        }

        var category = Rune.GetUnicodeCategory(rune);
        if (
            category
            is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.EnclosingMark
                or UnicodeCategory.Format
        )
        {
            return 0;
        }

        return IsWide(rune.Value) ? 2 : 1;
    }

    internal static string Clip(string text, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        var used = 0;
        var builder = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            int runeWidth = Width(rune);
            if (used + runeWidth > width)
            {
                break;
            }

            builder.Append(rune);
            used += runeWidth;
        }

        return builder.ToString();
    }

    private static bool IsWide(int value) =>
        value
            is >= 0x1100
                and <= 0x115F
                or >= 0x2E80
                and <= 0x303E
                or >= 0x3041
                and <= 0x33FF
                or >= 0x3400
                and <= 0x4DBF
                or >= 0x4E00
                and <= 0x9FFF
                or >= 0xA000
                and <= 0xA4CF
                or >= 0xA960
                and <= 0xA97F
                or >= 0xAC00
                and <= 0xD7A3
                or >= 0xF900
                and <= 0xFAFF
                or >= 0xFE10
                and <= 0xFE19
                or >= 0xFE30
                and <= 0xFE6F
                or >= 0xFF00
                and <= 0xFF60
                or >= 0xFFE0
                and <= 0xFFE6
                or >= 0x1F300
                and <= 0x1F64F
                or >= 0x1F680
                and <= 0x1F6FF
                or >= 0x1F900
                and <= 0x1F9FF
                or >= 0x1FA70
                and <= 0x1FAFF
                or >= 0x20000
                and <= 0x3FFFD;
}

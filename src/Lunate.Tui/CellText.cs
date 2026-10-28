using System.Buffers;
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

    /// <summary>
    /// One physical line from arbitrary text: whitespace runs (space, tab, CR, LF) collapse to
    /// single spaces, any remaining C0 control characters (U+0000–U+001F, U+007F) are dropped, and
    /// the result is trimmed. Empty output means there is nothing printable to show.
    /// </summary>
    internal static string SingleLine(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (char c in text)
        {
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (c <= '\u001f' || c == '\u007f')
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
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

    /// <summary>
    /// Clips text in the middle so both its start and its end survive: the result is the head, an
    /// ellipsis and the tail, measured in cells and never wider than <paramref name="width"/>. The
    /// head takes the extra cell when the budget is odd. Whole runes only.
    /// </summary>
    internal static string MiddleClip(string text, int width, bool unicode = true)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (Width(text) <= width)
        {
            return text;
        }

        string ellipsis = unicode ? "\u2026" : "...";
        int ellipsisWidth = Width(ellipsis);
        if (ellipsisWidth >= width)
        {
            return Clip(text, width);
        }

        int available = width - ellipsisWidth;
        int headWidth = (available + 1) / 2;
        int tailWidth = available / 2;
        return Clip(text, headWidth) + ellipsis + Tail(text, tailWidth);
    }

    /// <summary>
    /// Splits text into physical lines no wider than <paramref name="width"/> cells, preferring a
    /// space, then a path separator, then a rune boundary. No character is dropped, so the
    /// concatenation of the lines is always the input; a rune wider than the width still gets a
    /// line of its own so the wrap can make progress. <see cref="int.MaxValue"/> returns the text
    /// unchanged on one line.
    /// </summary>
    internal static IReadOnlyList<string> Wrap(string text, int width)
    {
        if (string.IsNullOrEmpty(text) || width == int.MaxValue)
        {
            return [text];
        }

        if (width <= 0)
        {
            return [string.Empty];
        }

        var lines = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            int used = 0;
            int index = start;
            int spaceBreak = -1;
            int pathBreak = -1;
            int overflow = text.Length;
            while (index < text.Length)
            {
                if (
                    Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out int consumed)
                    != OperationStatus.Done
                )
                {
                    break;
                }

                int runeWidth = Width(rune);
                if (used + runeWidth > width)
                {
                    overflow = index;
                    break;
                }

                used += runeWidth;
                index += consumed;
                if (rune.Value == ' ')
                {
                    spaceBreak = index;
                }
                else if (rune.Value is '/' or '\\')
                {
                    pathBreak = index;
                }
            }

            if (index >= text.Length)
            {
                lines.Add(text[start..]);
                break;
            }

            int end = overflow;
            if (spaceBreak > start)
            {
                end = spaceBreak;
            }
            else if (pathBreak > start)
            {
                end = pathBreak;
            }

            if (end <= start)
            {
                // A rune wider than the whole frame cannot fit anywhere; it still makes progress.
                end = start + RuneLength(text, start);
            }

            lines.Add(text[start..end]);
            start = end;
        }

        return lines;
    }

    private static int RuneLength(string text, int index) =>
        Rune.DecodeFromUtf16(text.AsSpan(index), out _, out int consumed) == OperationStatus.Done
            ? consumed
            : 1;

    private static string Tail(string text, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        var runes = text.EnumerateRunes().ToArray();
        var builder = new StringBuilder();
        var used = 0;
        for (var i = runes.Length - 1; i >= 0; i--)
        {
            int runeWidth = Width(runes[i]);
            if (used + runeWidth > width)
            {
                break;
            }

            builder.Insert(0, runes[i].ToString());
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

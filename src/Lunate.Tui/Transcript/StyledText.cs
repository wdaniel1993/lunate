using System.Text;

namespace Lunate.Tui;

/// <summary>
/// Word-wraps styled lines to a cell width: an embedded newline splits first, a line wider than the
/// width breaks greedily after its last word (the break space is dropped) and a word that does not
/// fit alone hard-breaks mid-run. Span styles carry across breaks, and spans that end up with the
/// same style coalesce. Pure: the same lines and width always wrap the same way, and no output line
/// is wider than the width unless a single rune cannot fit it.
/// </summary>
internal static class StyledText
{
    public static List<StyledLine> Wrap(IReadOnlyList<StyledLine> lines, int width)
    {
        ArgumentNullException.ThrowIfNull(lines);

        int limit = Math.Max(1, width);
        var result = new List<StyledLine>();
        foreach (StyledLine line in lines)
        {
            foreach (List<StyledRune> segment in Segments(line))
            {
                WrapSegment(result, segment, limit);
            }
        }

        return result;
    }

    private static void WrapSegment(List<StyledLine> result, List<StyledRune> segment, int limit)
    {
        if (Width(segment) <= limit)
        {
            result.Add(Build(segment));
            return;
        }

        var current = new List<StyledRune>();
        var used = 0;
        var index = 0;
        var atStart = true;
        while (index < segment.Count)
        {
            bool spaces = IsSpace(segment[index]);
            int start = index;
            do
            {
                index++;
            } while (index < segment.Count && IsSpace(segment[index]) == spaces);

            int tokenWidth = Width(segment, start, index);
            if (spaces)
            {
                // A space run separates words; it rides along while it fits and is dropped at a
                // break. A run at the segment's start is indentation — content, not a break.
                if ((atStart || used > 0) && used + tokenWidth <= limit)
                {
                    current.AddRange(segment.GetRange(start, index - start));
                    used += tokenWidth;
                }

                continue;
            }

            atStart = false;

            if (used > 0 && used + tokenWidth > limit)
            {
                Emit(result, current);
                current.Clear();
                used = 0;
            }

            for (var position = start; position < index; position++)
            {
                int runeWidth = CellText.Width(segment[position].Rune);
                if (used > 0 && used + runeWidth > limit)
                {
                    Emit(result, current);
                    current.Clear();
                    used = 0;
                }

                current.Add(segment[position]);
                used += runeWidth;
            }
        }

        Emit(result, current);
    }

    /// <summary>The line's segments: an embedded newline starts a new one.</summary>
    private static IEnumerable<List<StyledRune>> Segments(StyledLine line)
    {
        var current = new List<StyledRune>();
        foreach (var (text, style) in line.Spans)
        {
            foreach (Rune rune in text.EnumerateRunes())
            {
                if (rune.Value == '\n')
                {
                    yield return current;
                    current = [];
                    continue;
                }

                current.Add(new StyledRune(rune, style));
            }
        }

        yield return current;
    }

    /// <summary>One output line: trailing spaces go, adjacent same-style runes share one span.</summary>
    private static void Emit(List<StyledLine> result, List<StyledRune> runes)
    {
        while (runes.Count > 0 && IsSpace(runes[^1]))
        {
            runes.RemoveAt(runes.Count - 1);
        }

        result.Add(Build(runes));
    }

    private static StyledLine Build(IReadOnlyList<StyledRune> runes)
    {
        var line = new StyledLine();
        var text = new StringBuilder();
        SpanStyle? style = null;
        foreach (StyledRune rune in runes)
        {
            if (style is { } current && current != rune.Style)
            {
                line.Add(text.ToString(), current);
                text.Clear();
            }

            style = rune.Style;
            text.Append(rune.Rune.ToString());
        }

        if (style is { } last)
        {
            line.Add(text.ToString(), last);
        }

        return line;
    }

    private static int Width(IReadOnlyList<StyledRune> runes)
    {
        var width = 0;
        foreach (StyledRune rune in runes)
        {
            width += CellText.Width(rune.Rune);
        }

        return width;
    }

    private static int Width(IReadOnlyList<StyledRune> runes, int start, int end)
    {
        var width = 0;
        for (var index = start; index < end; index++)
        {
            width += CellText.Width(runes[index].Rune);
        }

        return width;
    }

    private static bool IsSpace(StyledRune rune) => rune.Rune.Value == ' ';

    private readonly record struct StyledRune(Rune Rune, SpanStyle Style);
}

using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// The committed user block: the submitted text as one scrollback block with a <c>&gt; </c>
/// prefix in the theme's user colour, plain text, a two-space hanging indent on wrapped lines and
/// one blank line after. Wrapping is cell-aware and greedy; a single word longer than the width
/// clips instead of overflowing. The renderer is a pure function of text, width and colour.
/// </summary>
internal sealed class UserEchoRenderer
{
    /// <summary>The visual spec's dark user value; the theme card moves this behind its resolver.</summary>
    internal const string DarkHex = "#87afd7";

    private const string Prefix = "> ";
    private const string Indent = "  ";

    private static readonly SpanStyle UserSpan = new(Color: DarkHex);
    private static readonly SpanStyle SixteenColorSpan = new(Color: "blue");

    public IRenderable Render(
        string text,
        int width,
        ColorSystemSupport color = ColorSystemSupport.TrueColor
    )
    {
        var builder = new StringBuilder();
        foreach (StyledLine line in Lines(text, width, color))
        {
            builder.Append(line.ToMarkup(trimTrailingWhitespace: false)).Append('\n');
        }

        return new Markup(builder.Append('\n').ToString());
    }

    internal IReadOnlyList<StyledLine> Lines(
        string text,
        int width,
        ColorSystemSupport color = ColorSystemSupport.TrueColor
    )
    {
        int columns = Math.Max(1, width);
        int textWidth = Math.Max(1, columns - Prefix.Length);
        SpanStyle prefixStyle = SpanFor(color);
        var lines = new List<StyledLine>();
        foreach ((string wrapped, bool first) in Wrap(text, textWidth))
        {
            var line = new StyledLine();
            line.Add(first ? Prefix : Indent, first ? prefixStyle : SpanStyle.Plain);
            line.Add(wrapped, SpanStyle.Plain);
            lines.Add(line.Clip(columns));
        }

        return lines;
    }

    private static SpanStyle SpanFor(ColorSystemSupport color) =>
        color switch
        {
            ColorSystemSupport.NoColors => SpanStyle.Plain,
            ColorSystemSupport.TrueColor or ColorSystemSupport.EightBit => UserSpan,
            _ => SixteenColorSpan,
        };

    /// <summary>
    /// Greedy word wrap at <paramref name="width"/> cells, keeping the text verbatim: space runs,
    /// leading and trailing spaces and blank hard lines all survive. One space is consumed at a
    /// soft break (it becomes the line break); a run wider than the width stays on one line and is
    /// clipped by the caller (the tool-block rule), never broken across lines.
    /// </summary>
    private static IReadOnlyList<(string Text, bool First)> Wrap(string text, int width)
    {
        var lines = new List<(string Text, bool First)>();
        foreach (string hard in text.Split('\n'))
        {
            var current = new StringBuilder();
            var used = 0;
            int index = 0;
            while (index < hard.Length)
            {
                int start = index;
                bool spaces = hard[index] == ' ';
                while (index < hard.Length && (hard[index] == ' ') == spaces)
                {
                    index++;
                }

                string token = hard[start..index];
                if (spaces)
                {
                    current.Append(token);
                    used += token.Length;
                    continue;
                }

                int wordWidth = CellText.Width(token);
                if (used > 0 && used + wordWidth > width)
                {
                    string line = current.ToString();
                    if (line.EndsWith(' '))
                    {
                        line = line[..^1];
                    }

                    lines.Add((line, lines.Count == 0));
                    current.Clear();
                    used = 0;
                }

                current.Append(token);
                used += wordWidth;
            }

            lines.Add((current.ToString(), lines.Count == 0));
        }

        return lines;
    }
}

using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// The committed thinking block: one collapsed <c>✦ thought for &lt;n&gt;s</c> line by default, or
/// the full reasoning text wrapped to the terminal width with a two-space indent when the
/// <c>thinkingFull</c> setting is on. The text is escaped for markup and painted in
/// <see cref="ThinkingColor"/>; the scrollback console drops the attribute under NO_COLOR, where
/// the glyph and the words carry the meaning.
/// </summary>
public sealed class ThinkingCommitRenderer
{
    /// <summary>The two-space indent every full-text line carries (design D8).</summary>
    private const string Indent = "  ";

    public IRenderable Render(ThinkingCommitModel commit, int width)
    {
        ArgumentNullException.ThrowIfNull(commit);
        IReadOnlyList<StyledLine> lines = commit.Full
            ? FullLines(commit.Text, width)
            : [Summary(commit.ElapsedSeconds)];
        var builder = new StringBuilder();
        foreach (StyledLine line in lines)
        {
            // Blank lines (an empty full text, leading or trailing hard breaks) commit nothing.
            if (!line.IsBlank)
            {
                builder.Append(line.ToMarkup()).Append('\n');
            }
        }

        return new Markup(builder.ToString());
    }

    private static StyledLine Summary(int elapsedSeconds)
    {
        var line = new StyledLine();
        line.Add(
            FormattableString.Invariant($"✦ thought for {elapsedSeconds}s"),
            ThinkingColor.Span
        );
        return line;
    }

    private static IReadOnlyList<StyledLine> FullLines(string text, int width)
    {
        var lines = new List<StyledLine>();
        foreach (string wrapped in Wrap(text, Math.Max(1, width - Indent.Length)))
        {
            var line = new StyledLine();
            line.Add(Indent + wrapped, ThinkingColor.Span);
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// Greedy word wrap at <paramref name="width"/> cells: a line breaks at its last space, and a
    /// run longer than the width breaks mid-run, so no committed line overflows the terminal.
    /// </summary>
    private static IReadOnlyList<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (string hard in text.Split('\n'))
        {
            var current = new StringBuilder();
            var used = 0;
            foreach (string word in hard.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int wordWidth = CellText.Width(word);
                if (used > 0 && used + 1 + wordWidth > width)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    used = 0;
                }

                if (used > 0)
                {
                    current.Append(' ');
                    used++;
                }

                if (wordWidth <= width)
                {
                    current.Append(word);
                    used += wordWidth;
                    continue;
                }

                foreach (var rune in word.EnumerateRunes())
                {
                    int runeWidth = CellText.Width(rune);
                    if (used > 0 && used + runeWidth > width)
                    {
                        lines.Add(current.ToString());
                        current.Clear();
                        used = 0;
                    }

                    current.Append(rune);
                    used += runeWidth;
                }
            }

            lines.Add(current.ToString());
        }

        return lines;
    }
}

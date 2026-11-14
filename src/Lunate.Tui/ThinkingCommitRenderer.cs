using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// The committed thinking block: one collapsed <c>✦ thought for &lt;n&gt;s</c> line by default, or
/// the full reasoning text wrapped to the terminal width with a two-space indent when the
/// <c>thinkingFull</c> setting is on. The text is escaped for markup and painted in the theme's
/// <c>notice</c> token for the caller's capability — 16-colour mode selects its pinned fallback —
/// and the scrollback console drops the attribute under NO_COLOR, where the glyph and the words
/// carry the meaning.
/// </summary>
public sealed class ThinkingCommitRenderer
{
    /// <summary>The two-space indent every full-text line carries (design D8).</summary>
    private const string Indent = "  ";

    public IRenderable Render(
        ThinkingCommitModel commit,
        int width,
        ColorSystemSupport color = ColorSystemSupport.TrueColor
    ) => Render(commit, width, new TerminalCapabilities(color, Unicode: true));

    internal IRenderable Render(
        ThinkingCommitModel commit,
        int width,
        TerminalCapabilities capabilities
    )
    {
        var builder = new StringBuilder();
        foreach (StyledLine line in Lines(commit, width, capabilities))
        {
            // Blank lines (an empty full text, leading or trailing hard breaks) commit nothing.
            if (!line.IsBlank)
            {
                builder.Append(line.ToMarkup()).Append('\n');
            }
        }

        return new Markup(builder.ToString());
    }

    /// <summary>
    /// The commit's styled lines: the collapsed summary, or the full reasoning text wrapped to the
    /// width with its indent. The transcript viewer renders from here with the commit forced full.
    /// </summary>
    internal IReadOnlyList<StyledLine> Lines(
        ThinkingCommitModel commit,
        int width,
        TerminalCapabilities capabilities
    )
    {
        ArgumentNullException.ThrowIfNull(commit);
        return commit.Full
            ? FullLines(commit.Text, width, capabilities)
            : [Summary(commit.ElapsedSeconds, capabilities)];
    }

    private static StyledLine Summary(int elapsedSeconds, TerminalCapabilities capabilities)
    {
        var line = new StyledLine();
        line.Add(
            FormattableString.Invariant($"✦ thought for {elapsedSeconds}s"),
            capabilities.Span(ThemeToken.Notice)
        );
        return line;
    }

    private static IReadOnlyList<StyledLine> FullLines(
        string text,
        int width,
        TerminalCapabilities capabilities
    )
    {
        SpanStyle span = capabilities.Span(ThemeToken.Notice);
        var lines = new List<StyledLine>();
        foreach (string wrapped in Wrap(text, Math.Max(1, width - Indent.Length)))
        {
            var line = new StyledLine();
            line.Add(Indent + wrapped, span);
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

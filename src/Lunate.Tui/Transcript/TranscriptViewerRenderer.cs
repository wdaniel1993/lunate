using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// The transcript viewer's full-screen frame: every committed entry in commit order — user echo,
/// assistant markdown, thinking and tool blocks with their full outputs and diffs — wrapped to the
/// width, with mid-shortened paths and a footer carrying the keys and the position. The frame is a
/// pure function of the transcript, the size and the scroll offset: no clock, no console.
/// </summary>
public sealed class TranscriptViewerRenderer
{
    internal const string UnicodeKeys =
        "\u2191\u2193 scroll \u00b7 PgUp/PgDn \u00b7 Home/End \u00b7 g/G \u00b7 q close";

    internal const string AsciiKeys =
        "up/down scroll \u00b7 PgUp/PgDn \u00b7 Home/End \u00b7 g/G \u00b7 q close";

    /// <summary>The footer is one line; the content gets the rows above it.</summary>
    private const int FooterLines = 1;

    private readonly TerminalCapabilities _capabilities;
    private readonly ToolBlockRenderer _blocks;
    private readonly MarkdownRenderer _markdown;
    private readonly ThinkingCommitRenderer _thinking = new();

    /// <summary>The Unicode defaults; the interactive session passes its detected capabilities.</summary>
    public TranscriptViewerRenderer()
        : this(new TerminalCapabilities(ColorSystemSupport.Detect, Unicode: true)) { }

    internal TranscriptViewerRenderer(
        TerminalCapabilities capabilities,
        string? homeDirectory = null
    )
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _capabilities = capabilities;
        _blocks = new ToolBlockRenderer(capabilities, homeDirectory);
        _markdown = new MarkdownRenderer(capabilities);
    }

    /// <summary>The frame as markup, opening at the latest content (<paramref name="scrolledLines"/> 0).</summary>
    public IRenderable Render(TranscriptModel transcript, ConsoleSize size, int scrolledLines = 0)
    {
        var builder = new StringBuilder();
        foreach (StyledLine line in Lines(transcript, size, scrolledLines))
        {
            builder.Append(line.ToMarkup()).Append('\n');
        }

        return new Markup(builder.ToString());
    }

    /// <summary>
    /// Exactly <c>max(1, size.Rows)</c> lines: the document window ending one line above the
    /// bottom, blank-padded at the bottom, then the footer. <paramref name="scrolledLines"/> moves
    /// the window up from the latest content and clamps to the document.
    /// </summary>
    internal IReadOnlyList<StyledLine> Lines(
        TranscriptModel transcript,
        ConsoleSize size,
        int scrolledLines
    )
    {
        ArgumentNullException.ThrowIfNull(transcript);
        int columns = Math.Max(1, size.Columns);
        int rows = Math.Max(1, size.Rows);
        int contentHeight = Math.Max(0, rows - FooterLines);
        List<StyledLine> document = Document(transcript, columns);
        int maxTop = Math.Max(0, document.Count - contentHeight);
        int top = maxTop - Math.Clamp(scrolledLines, 0, maxTop);

        var frame = new List<StyledLine>(rows);
        for (var index = 0; index < contentHeight; index++)
        {
            int at = top + index;
            frame.Add(at < document.Count ? document[at] : new StyledLine());
        }

        frame.Add(Footer(columns, top, maxTop));
        return frame;
    }

    /// <summary>The whole transcript as one document: each entry's lines, a blank between entries.</summary>
    private List<StyledLine> Document(TranscriptModel transcript, int columns)
    {
        var document = new List<StyledLine>();
        var first = true;
        foreach (TranscriptEntry entry in transcript.Entries)
        {
            if (!first)
            {
                document.Add(new StyledLine());
            }

            first = false;
            document.AddRange(StyledText.Wrap(EntryLines(entry, columns), columns));
        }

        return document;
    }

    private IReadOnlyList<StyledLine> EntryLines(TranscriptEntry entry, int columns) =>
        entry switch
        {
            TranscriptUserEntry user => [Echo(user.Text)],
            TranscriptAssistantEntry assistant => _markdown.Lines(assistant.Markdown),
            TranscriptThinkingEntry thinking => ThinkingLines(thinking.Commit, columns),
            TranscriptToolEntry tool => _blocks.Lines(tool.Block, fullOutput: true),
            _ => [],
        };

    /// <summary>The same echo the session's scrollback commits: the glyph and the text, dim.</summary>
    private StyledLine Echo(string text)
    {
        var line = new StyledLine();
        line.Add((_capabilities.Unicode ? "\u00bb " : "> ") + text, SpanStyle.Dim);
        return line;
    }

    /// <summary>The viewer shows the reasoning in full; only blank text falls back to the summary.</summary>
    private IReadOnlyList<StyledLine> ThinkingLines(ThinkingCommitModel commit, int columns)
    {
        var shown = commit with { Full = !string.IsNullOrWhiteSpace(commit.Text) };
        return _thinking.Lines(shown, columns, _capabilities.Color);
    }

    /// <summary>Keys left, position right, dim; clipped so the line never passes the width.</summary>
    private StyledLine Footer(int columns, int top, int maxTop)
    {
        string keys = _capabilities.Unicode ? UnicodeKeys : AsciiKeys;
        string percent = string.Create(CultureInfo.InvariantCulture, $"{Percent(top, maxTop)}%");
        string visibleKeys = CellText.Clip(keys, columns - CellText.Width(percent) - 1);

        var line = new StyledLine();
        line.Add(visibleKeys, SpanStyle.Dim);
        int padding = columns - CellText.Width(visibleKeys) - CellText.Width(percent);
        if (padding > 0)
        {
            line.Add(new string(' ', padding), SpanStyle.Plain);
        }

        string visiblePercent = CellText.Clip(percent, columns - CellText.Width(visibleKeys));
        if (visiblePercent.Length > 0)
        {
            line.Add(visiblePercent, SpanStyle.Dim);
        }

        return line;
    }

    private static int Percent(int top, int maxTop) => maxTop == 0 ? 100 : top * 100 / maxTop;
}

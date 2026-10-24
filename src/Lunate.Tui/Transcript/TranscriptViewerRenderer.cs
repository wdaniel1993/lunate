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

    internal const string AsciiKeys = "up/down scroll - PgUp/PgDn - Home/End - g/G - q close";

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
    ) => Lines(transcript, size, scrolledLines, options: null);

    /// <summary>
    /// <see cref="Lines(TranscriptModel,ConsoleSize,int)"/> with the pick mode's surface: the
    /// selected entry is marked, and the footer shows the caller's hint and position instead of the
    /// viewer's keys. The same document, wrapping and rendering; only the marking differs.
    /// </summary>
    internal IReadOnlyList<StyledLine> Lines(
        TranscriptModel transcript,
        ConsoleSize size,
        int scrolledLines,
        TranscriptViewOptions? options
    )
    {
        ArgumentNullException.ThrowIfNull(transcript);
        int columns = Math.Max(1, size.Columns);
        int rows = Math.Max(1, size.Rows);
        int contentHeight = Math.Max(0, rows - FooterLines);
        List<int> tops = [];
        List<StyledLine> document = Document(transcript, columns, tops);
        if (
            options?.SelectedEntry is { } selected
            && selected >= 0
            && selected < tops.Count
            && tops[selected] < document.Count
        )
        {
            document[tops[selected]] = Marked(document[tops[selected]], columns);
        }

        int maxTop = Math.Max(0, document.Count - contentHeight);
        int top = maxTop - Math.Clamp(scrolledLines, 0, maxTop);

        var frame = new List<StyledLine>(rows);
        for (var index = 0; index < contentHeight; index++)
        {
            int at = top + index;
            frame.Add(at < document.Count ? document[at] : new StyledLine());
        }

        frame.Add(Footer(columns, top, maxTop, options));
        return frame;
    }

    /// <summary>
    /// The document line each entry starts on, in entry order: the pick mode scrolls the selected
    /// entry into view with it. Same document as <see cref="Lines(TranscriptModel,ConsoleSize,int)"/>.
    /// </summary>
    internal IReadOnlyList<int> EntryTops(TranscriptModel transcript, ConsoleSize size)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        List<int> tops = [];
        _ = Document(transcript, Math.Max(1, size.Columns), tops);
        return tops;
    }

    /// <summary>
    /// The furthest the window can move up from the latest content for this transcript and size;
    /// 0 when the document fits the frame. The navigation bindings clamp against it.
    /// </summary>
    internal int MaxScrolledLines(TranscriptModel transcript, ConsoleSize size)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        int columns = Math.Max(1, size.Columns);
        int contentHeight = Math.Max(0, Math.Max(1, size.Rows) - FooterLines);
        return Math.Max(0, Document(transcript, columns).Count - contentHeight);
    }

    /// <summary>The whole transcript as one document: each entry's lines, a blank between entries.</summary>
    private List<StyledLine> Document(
        TranscriptModel transcript,
        int columns,
        List<int>? entryTops = null
    )
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
            entryTops?.Add(document.Count);
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
            TranscriptToolEntry tool => _blocks.Lines(tool.Block, fullOutput: true, width: columns),
            TranscriptTreeRowEntry row => [TreeRow(row)],
            _ => [],
        };

    /// <summary>
    /// A tree row: a two-character gutter — <c>*</c> on the active path, <c>-</c> off it — then two
    /// spaces of indentation per depth and the row's own text. An off-path row is dim: still
    /// readable, and plainly not on the branch the model sees.
    /// </summary>
    private static StyledLine TreeRow(TranscriptTreeRowEntry row)
    {
        var line = new StyledLine();
        string gutter = row.OnActivePath ? "* " : "- ";
        line.Add(
            gutter + new string(' ', Math.Max(0, row.Depth) * 2) + row.Text,
            row.OnActivePath ? SpanStyle.Plain : SpanStyle.Dim
        );
        return line;
    }

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
    private StyledLine Footer(
        int columns,
        int top,
        int maxTop,
        TranscriptViewOptions? options = null
    )
    {
        string keys = options?.Hint ?? (_capabilities.Unicode ? UnicodeKeys : AsciiKeys);
        string position = options is null
            ? string.Create(CultureInfo.InvariantCulture, $"{Percent(top, maxTop)}%")
            : string.Create(CultureInfo.InvariantCulture, $"{options.Position} of {options.Count}");
        string visibleKeys = CellText.Clip(keys, columns - CellText.Width(position) - 1);

        var line = new StyledLine();
        line.Add(visibleKeys, SpanStyle.Dim);
        int padding = columns - CellText.Width(visibleKeys) - CellText.Width(position);
        if (padding > 0)
        {
            line.Add(new string(' ', padding), SpanStyle.Plain);
        }

        string visiblePosition = CellText.Clip(position, columns - CellText.Width(visibleKeys));
        if (visiblePosition.Length > 0)
        {
            line.Add(visiblePosition, SpanStyle.Dim);
        }

        return line;
    }

    /// <summary>The selected entry's first line with its marker: the one visual difference of pick mode.</summary>
    private StyledLine Marked(StyledLine line, int columns)
    {
        var marked = new StyledLine();
        marked.Add(_capabilities.Unicode ? "\u25b8 " : "> ", SpanStyle.Key);
        marked.Append(line);
        return marked.Clip(columns);
    }

    private static int Percent(int top, int maxTop) => maxTop == 0 ? 100 : top * 100 / maxTop;
}

/// <summary>
/// The pick mode's view options: which entry carries the selection marker, the one-based position
/// for the footer and the hint that replaces the viewer's key line.
/// </summary>
internal sealed record TranscriptViewOptions(
    int? SelectedEntry = null,
    int Position = 0,
    int Count = 0,
    string? Hint = null
);

using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>How much of a tool block the scrollback shows: its header alone or its details too.</summary>
public enum ToolDetails
{
    /// <summary>The header line only, with a <c>ctrl+o</c> hint when details exist (default).</summary>
    Collapsed,

    /// <summary>The header line, the bounded output excerpt and, when present, the diff panel.</summary>
    Expanded,
}

/// <summary>
/// Renders a tool call as a borderless scrollback block: a header with a status glyph, the tool
/// name, its compact summary and meta, followed by a bounded output excerpt and, when present, a
/// diff panel. Collapsed blocks show the header alone with a <c>ctrl+o</c> hint. Every
/// user-derived string goes through markup escaping before assembly.
/// </summary>
public sealed class ToolBlockRenderer
{
    internal static readonly SpanStyle StatusOk = new(Color: "green");
    internal static readonly SpanStyle StatusError = new(Color: "red");
    internal static readonly SpanStyle StatusRunning = SpanStyle.Dim;
    internal static readonly SpanStyle Accent = new(Color: "cyan");
    internal static readonly SpanStyle Muted = SpanStyle.Dim;
    internal static readonly SpanStyle Warning = new(Color: "yellow");

    private readonly bool _unicode;
    private readonly string _home;

    /// <summary>The Unicode defaults; the interactive session passes its detected capabilities.</summary>
    public ToolBlockRenderer()
        : this(new TerminalCapabilities(ColorSystemSupport.Detect, Unicode: true)) { }

    internal ToolBlockRenderer(TerminalCapabilities capabilities, string? homeDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _unicode = capabilities.Unicode;
        _home = homeDirectory ?? PathText.Home();
    }

    public IRenderable Render(ToolBlockModel block) =>
        Render(block, int.MaxValue, ToolDetails.Collapsed);

    /// <summary>
    /// The scrollback block narrowed for the terminal: below 60 columns the header drops its
    /// argument summary and carries glyph, name and meta only.
    /// </summary>
    public IRenderable Render(ToolBlockModel block, int width) =>
        Render(block, width, ToolDetails.Collapsed);

    /// <summary>The scrollback block with the requested level of detail.</summary>
    public IRenderable Render(ToolBlockModel block, int width, ToolDetails details)
    {
        var lines = Lines(block, fullOutput: false, width, details);
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line.ToMarkup()).Append('\n');
        }

        return new Markup(builder.ToString());
    }

    /// <summary>
    /// The block's styled lines: the header, then the output and the diff panel as the details
    /// allow. The scrollback excerpts the output (<paramref name="fullOutput"/> is <c>false</c>)
    /// and collapses it to the header alone unless <paramref name="details"/> is
    /// <see cref="ToolDetails.Expanded"/>; the transcript viewer shows every line — a
    /// <c>read</c> block's output included, which the scrollback hides.
    /// </summary>
    internal IReadOnlyList<StyledLine> Lines(
        ToolBlockModel block,
        bool fullOutput,
        int width = int.MaxValue,
        ToolDetails details = ToolDetails.Collapsed
    )
    {
        ArgumentNullException.ThrowIfNull(block);

        var lines = new List<StyledLine>
        {
            Header(block, width, showHint: !fullOutput && details == ToolDetails.Collapsed),
        };
        if (fullOutput)
        {
            AppendOutput(lines, block.Output, ExcerptMode.Full, block.ChildResult);
        }
        else if (details == ToolDetails.Expanded && ShowsOutput(block))
        {
            AppendOutput(lines, block.Output, ExcerptModeFor(block), block.ChildResult);
        }

        if (block.Diff is not null && (fullOutput || details == ToolDetails.Expanded))
        {
            DiffRenderer.Append(lines, block.Diff);
        }

        return lines;
    }

    private StyledLine Header(ToolBlockModel block, int width, bool showHint)
    {
        var line = new StyledLine();
        line.Add(Glyph(block.Status), StatusStyle(block.Status));
        line.Add(" ", SpanStyle.Plain);
        line.Add(block.ToolName, Accent);
        bool compact = WidthBands.IsCompact(width);
        // A failed call's output is an error message, not file content: no line count from it.
        // An image read's placeholder is not a line either.
        string? resultOutput =
            block.Status == ToolBlockStatus.Error || block.IsImage ? null : block.Output;
        if (!compact)
        {
            string summary = ToolArgsSummary.Summarize(
                block.ToolName,
                block.ArgsSummary,
                resultOutput,
                _home,
                unicode: _unicode
            );
            if (summary.Length > 0)
            {
                line.Add("  " + summary, Muted);
            }
        }

        if (ToolBlockMeta.Build(block, _unicode, compact) is { Length: > 0 } meta)
        {
            line.Add((_unicode ? " \u00b7 " : " - ") + meta, Muted);
        }

        if (showHint && HasHiddenDetail(block))
        {
            line.Add((_unicode ? " \u00b7 " : " - ") + "ctrl+o", Muted);
        }

        return line.Clip(width);
    }

    /// <summary>
    /// A read block identifies what was read; a failed call keeps its message visible, and a
    /// successful image read shows its placeholder instead of hiding it with the text output. Only
    /// the block's image flag makes a placeholder an image: a text file can contain that text.
    /// </summary>
    private static bool ShowsOutput(ToolBlockModel block) =>
        block.ToolName != "read"
        || block.Status == ToolBlockStatus.Error
        || (block.IsImage && ImagePlaceholder.IsOnly(block.Output));

    private static ExcerptMode ExcerptModeFor(ToolBlockModel block) =>
        block.ToolName == "bash" ? ExcerptMode.TailOnly : ExcerptMode.HeadAndTail;

    /// <summary>Whether expanded details would show output or a diff beyond the header.</summary>
    private bool HasHiddenDetail(ToolBlockModel block) =>
        (
            ShowsOutput(block)
            && OutputExcerpt.Build(block.Output, ExcerptModeFor(block), _unicode).Count > 0
        ) || (block.Diff is not null && DiffRenderer.Parse(block.Diff).Count > 0);

    private void AppendOutput(List<StyledLine> lines, string? output, ExcerptMode mode, bool indent)
    {
        foreach (var excerpt in OutputExcerpt.Build(output, mode, _unicode))
        {
            var line = new StyledLine();
            line.Add(
                indent ? "  " + excerpt.Text : excerpt.Text,
                excerpt.IsMarker ? Warning
                    : ImagePlaceholder.IsOnly(excerpt.Text) ? Muted
                    : SpanStyle.Plain
            );
            lines.Add(line);
        }
    }

    private string Glyph(ToolBlockStatus status) =>
        status switch
        {
            ToolBlockStatus.Ok => _unicode ? "\u2713" : "+",
            ToolBlockStatus.Error => _unicode ? "\u2717" : "x",
            _ => _unicode ? "\u2026" : "...",
        };

    private static SpanStyle StatusStyle(ToolBlockStatus status) =>
        status switch
        {
            ToolBlockStatus.Running => StatusRunning,
            ToolBlockStatus.Ok => StatusOk,
            ToolBlockStatus.Error => StatusError,
            _ => SpanStyle.Plain,
        };
}

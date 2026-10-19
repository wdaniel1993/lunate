using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// Renders a tool call as a borderless scrollback block: a header with a status glyph, the tool
/// name, its compact summary and meta, followed by a bounded output excerpt and, when present, a
/// diff panel. Every user-derived string goes through markup escaping before assembly.
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

    /// <summary>The Unicode defaults; the interactive session passes its detected capabilities.</summary>
    public ToolBlockRenderer()
        : this(new TerminalCapabilities(ColorSystemSupport.Detect, Unicode: true)) { }

    internal ToolBlockRenderer(TerminalCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _unicode = capabilities.Unicode;
    }

    public IRenderable Render(ToolBlockModel block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var lines = new List<StyledLine> { Header(block) };
        if (ShowsOutput(block))
        {
            AppendOutput(lines, block.Output, block.ToolName == "bash");
        }

        if (block.Diff is not null)
        {
            DiffRenderer.Append(lines, block.Diff);
        }

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line.ToMarkup()).Append('\n');
        }

        return new Markup(builder.ToString());
    }

    private StyledLine Header(ToolBlockModel block)
    {
        var line = new StyledLine();
        line.Add(Glyph(block.Status), StatusStyle(block.Status));
        line.Add(" ", SpanStyle.Plain);
        line.Add(block.ToolName, Accent);
        // A failed call's output is an error message, not file content: no line count from it.
        string? resultOutput = block.Status == ToolBlockStatus.Error ? null : block.Output;
        string summary = ToolArgsSummary.Summarize(block.ToolName, block.ArgsSummary, resultOutput);
        if (summary.Length > 0)
        {
            line.Add("  " + summary, Muted);
        }

        if (ToolBlockMeta.Build(block) is { Length: > 0 } meta)
        {
            line.Add(" \u00b7 " + meta, Muted);
        }

        return line;
    }

    /// <summary>A read block identifies what was read; a failed call keeps its message visible.</summary>
    private static bool ShowsOutput(ToolBlockModel block) =>
        block.ToolName != "read" || block.Status == ToolBlockStatus.Error;

    private void AppendOutput(List<StyledLine> lines, string? output, bool tailOnly)
    {
        var mode = tailOnly ? ExcerptMode.TailOnly : ExcerptMode.HeadAndTail;
        foreach (var excerpt in OutputExcerpt.Build(output, mode, _unicode))
        {
            var line = new StyledLine();
            line.Add(excerpt.Text, excerpt.IsMarker ? Warning : SpanStyle.Plain);
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

using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// Renders a tool call as a borderless scrollback block: a header with tool name, argument summary
/// and status, followed by a bounded output excerpt and, when present, a diff panel. Every
/// user-derived string goes through markup escaping before assembly.
/// </summary>
public sealed class ToolBlockRenderer
{
    internal static readonly SpanStyle StatusOk = new(Color: "green");
    internal static readonly SpanStyle StatusError = new(Color: "red");
    internal static readonly SpanStyle StatusRunning = SpanStyle.Dim;

    public IRenderable Render(ToolBlockModel block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var lines = new List<StyledLine> { Header(block) };
        AppendOutput(lines, block.Output);
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

    private static StyledLine Header(ToolBlockModel block)
    {
        var line = new StyledLine();
        line.Add("tool", SpanStyle.Plain);
        line.Add(" ", SpanStyle.Plain);
        line.Add(block.ToolName, SpanStyle.Plain);
        string summary = ToolArgsSummary.Summarize(block.ToolName, block.ArgsSummary);
        if (summary.Length > 0)
        {
            line.Add(" ", SpanStyle.Plain);
            line.Add(summary, SpanStyle.Plain);
        }

        line.Add(" ", SpanStyle.Plain);
        line.Add(StatusText(block.Status), StatusStyle(block.Status));
        return line;
    }

    private static void AppendOutput(List<StyledLine> lines, string? output)
    {
        foreach (var excerpt in OutputExcerpt.Build(output))
        {
            var line = new StyledLine();
            line.Add(excerpt.Text, excerpt.IsMarker ? SpanStyle.Dim : SpanStyle.Plain);
            lines.Add(line);
        }
    }

    private static string StatusText(ToolBlockStatus status) =>
        status switch
        {
            ToolBlockStatus.Running => "running\u2026",
            ToolBlockStatus.Ok => "ok",
            ToolBlockStatus.Error => "failed",
            _ => "unknown",
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

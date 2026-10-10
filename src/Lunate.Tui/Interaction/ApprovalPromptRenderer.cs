using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// Renders a pending approval as one borderless line:
/// <c>Allow &lt;tool&gt; &lt;summary&gt;?  [y]es  [n]o  [a]lways this session</c>. All user-derived
/// text is markup-escaped; the question marker is yellow.
/// </summary>
public sealed class ApprovalPromptRenderer
{
    internal static readonly SpanStyle Question = new(Color: "yellow");

    public IRenderable Render(ApprovalPromptModel prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var line = new StyledLine();
        line.Add("Allow ", SpanStyle.Plain);
        line.Add(prompt.ToolName, SpanStyle.Plain);
        string summary = ToolArgsSummary.Summarize(prompt.ToolName, prompt.ArgsSummary);
        if (summary.Length > 0)
        {
            line.Add(" ", SpanStyle.Plain);
            line.Add(summary, SpanStyle.Plain);
        }

        line.Add("?", Question);
        line.Add("  [y]es  [n]o  [a]lways this session", SpanStyle.Plain);
        return new Markup(line.ToMarkup() + "\n");
    }

    /// <summary>The prompt's plain (unstyled) line as shown in the live area.</summary>
    internal static string PlainText(ApprovalPromptModel prompt)
    {
        string summary = ToolArgsSummary.Summarize(prompt.ToolName, prompt.ArgsSummary);
        string question =
            summary.Length > 0
                ? $"Allow {prompt.ToolName} {summary}?"
                : $"Allow {prompt.ToolName}?";
        return question + "  [y]es  [n]o  [a]lways this session";
    }
}

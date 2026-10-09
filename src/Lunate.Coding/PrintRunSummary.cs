using System.Text;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// Tracks what a print run produced while its events stream: the last non-empty assistant text,
/// the terminal stop reason or error, and one stderr diagnostic per denied or error tool call.
/// </summary>
internal sealed class PrintRunSummary(ApprovalPolicy policy)
{
    private readonly Dictionary<string, StringBuilder> _openText = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _toolNames = new(StringComparer.Ordinal);

    /// <summary>The text of the last assistant message with content, or null when there was none.</summary>
    public string? FinalAnswer { get; private set; }

    /// <summary>The terminal stop reason of the run, or null when no finish event arrived.</summary>
    public string? StopReason { get; private set; }

    /// <summary>The run error message, or null when the run did not fail.</summary>
    public string? Error { get; private set; }

    /// <summary>Tracks the event; returns a stderr diagnostic for an error tool call, else null.</summary>
    public string? Observe(AgentEvent agentEvent)
    {
        switch (agentEvent)
        {
            case TextMessageStart start:
                _openText[start.MessageId] = new StringBuilder();
                break;
            case TextMessageContent content:
                OpenText(content.MessageId).Append(content.Text);
                break;
            case TextMessageEnd end:
                if (_openText.Remove(end.MessageId, out var completed) && completed.Length > 0)
                {
                    FinalAnswer = completed.ToString();
                }

                break;
            case ToolCallStart call:
                _toolNames[call.CallId] = call.ToolName;
                break;
            case ToolCallResult result when result.IsError:
                return Diagnostic(_toolNames.GetValueOrDefault(result.CallId, "?"), result.Output);
            case RunFinished finished:
                StopReason = finished.StopReason;
                break;
            case RunError error:
                Error = error.Message;
                break;
        }

        return null;
    }

    private StringBuilder OpenText(string messageId) =>
        _openText.TryGetValue(messageId, out var text)
            ? text
            : _openText[messageId] = new StringBuilder();

    private string Diagnostic(string toolName, string output) =>
        output.StartsWith("Denied:", StringComparison.Ordinal)
            ? $"tool '{toolName}' denied: approval policy '{PolicyText(policy)}' (pass --yolo to run unattended)"
        : FirstNonEmptyLine(output) is { } line ? $"tool '{toolName}' failed: {line}"
        : $"tool '{toolName}' failed";

    private static string PolicyText(ApprovalPolicy policy) =>
        policy switch
        {
            ApprovalPolicy.Ask => "ask",
            ApprovalPolicy.AutoEdit => "auto-edit",
            _ => policy.ToString(),
        };

    private static string? FirstNonEmptyLine(string output)
    {
        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r').Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return null;
    }
}

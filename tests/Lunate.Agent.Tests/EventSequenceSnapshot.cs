using System.Text.Encodings.Web;
using System.Text.Json;

namespace Lunate.Agent.Tests;

/// <summary>
/// Renders a run's events as a byte-stable, one-line-per-event snapshot. Generated ids (run and
/// message ids) are normalized; tool call ids come from the recorded session and stay as they are.
/// </summary>
internal static class EventSequenceSnapshot
{
    private static readonly JsonSerializerOptions QuoteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static string Serialize(IEnumerable<AgentEvent> events) =>
        string.Join("\n", events.Select(Line)) + "\n";

    private static string Line(AgentEvent agentEvent) =>
        agentEvent switch
        {
            RunStarted e => $"RunStarted({Run(e.RunId)})",
            RunFinished e => $"RunFinished({Run(e.RunId)}, {e.StopReason})",
            RunError e => $"RunError({Run(e.RunId)}, {Quote(e.Message)})",
            TextMessageStart e => $"TextMessageStart({Run(e.RunId)}, {Message(e.MessageId)})",
            TextMessageContent e =>
                $"TextMessageContent({Run(e.RunId)}, {Message(e.MessageId)}, {Quote(e.Text)})",
            TextMessageEnd e => $"TextMessageEnd({Run(e.RunId)}, {Message(e.MessageId)})",
            ToolCallStart e => $"ToolCallStart({Run(e.RunId)}, {e.CallId}, {e.ToolName})",
            ToolCallArgs e => $"ToolCallArgs({Run(e.RunId)}, {e.CallId}, {Quote(e.Args)})",
            ToolCallEnd e => $"ToolCallEnd({Run(e.RunId)}, {e.CallId})",
            ToolCallResult e =>
                $"ToolCallResult({Run(e.RunId)}, {e.CallId}, {Quote(e.Output)}, {Bool(e.IsError)})",
            _ => agentEvent.ToString() ?? agentEvent.GetType().Name,
        };

    private static string Run(string runId) =>
        runId.StartsWith("run_", StringComparison.Ordinal) ? "run" : runId;

    private static string Message(string messageId) =>
        messageId.StartsWith("msg_", StringComparison.Ordinal) ? "msg" : messageId;

    private static string Quote(string value) => JsonSerializer.Serialize(value, QuoteOptions);

    private static string Bool(bool value) => value ? "true" : "false";
}

using System.Text.Json;
using Acp.Schema;
using Lunate.Agent;

namespace Lunate.Protocols.Acp;

/// <summary>
/// Pure mapping from <see cref="AgentEvent"/> to ACP <c>session/update</c> payloads, following the
/// guide's event table. Events that do not become updates return null: run lifecycle maps into the
/// prompt response (<see cref="MapStopReason"/>), and the extension events the table marks
/// "not sent" are logged by the adapter.
/// </summary>
internal static class AcpEventMapper
{
    /// <summary>The stop reason a failed run reports; ACP has no error stop reason.</summary>
    internal static StopReason ErrorStopReason => StopReason.Refusal;

    internal static SessionUpdate? Map(AgentEvent agentEvent) =>
        agentEvent switch
        {
            TextMessageContent content => new AgentMessageChunk
            {
                Content = new TextContent { Text = content.Text },
            },
            ToolCallStart start => new ToolCallStartUpdate
            {
                ToolCallId = new ToolCallId(start.CallId),
                Title = start.ToolName,
                Status = ToolCallStatus.InProgress,
                Kind = ToolKind.Other,
            },
            ToolCallArgs args => new ToolCallUpdateUpdate
            {
                ToolCallId = new ToolCallId(args.CallId),
                RawInput = ParseArguments(args.Args),
            },
            ToolCallEnd end => new ToolCallUpdateUpdate { ToolCallId = new ToolCallId(end.CallId) },
            ToolCallResult result => new ToolCallUpdateUpdate
            {
                ToolCallId = new ToolCallId(result.CallId),
                Status = result.IsError ? ToolCallStatus.Failed : ToolCallStatus.Completed,
                Content =
                [
                    new ToolCallContentBlock { Content = new TextContent { Text = result.Output } },
                ],
            },
            _ => null,
        };

    /// <summary>Maps a Lunate stop reason (see <see cref="StopReasons"/>) onto the ACP vocabulary.</summary>
    internal static StopReason MapStopReason(string stopReason) =>
        stopReason switch
        {
            StopReasons.Stop => StopReason.EndTurn,
            StopReasons.Cancelled => StopReason.Cancelled,
            StopReasons.Length => StopReason.MaxTokens,
            StopReasons.StepLimit => StopReason.MaxTurnRequests,
            _ => StopReason.EndTurn,
        };

    private static JsonElement? ParseArguments(string args)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(args);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(args);
        }
    }
}

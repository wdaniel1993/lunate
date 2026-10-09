using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// Serializes one <see cref="AgentEvent"/> to one JSON line for <c>lunate -p --json</c>: camelCase,
/// invariant, no pretty printing. The switch covers every sealed event record and the reflection
/// test pins that set, so a new event type fails the test run. <c>details</c> is written as a JSON
/// element (the eval reads <c>matchTier</c> from edit details).
/// </summary>
internal static class PrintEventJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The event types the serializer handles; the reflection test pins this against the assembly.</summary>
    internal static IReadOnlyList<Type> HandledEventTypes =>
        [
            typeof(RunStarted),
            typeof(RunFinished),
            typeof(RunError),
            typeof(TextMessageStart),
            typeof(TextMessageContent),
            typeof(TextMessageEnd),
            typeof(ToolCallStart),
            typeof(ToolCallArgs),
            typeof(ToolCallEnd),
            typeof(ToolCallResult),
            typeof(ApprovalRequested),
            typeof(UsageUpdated),
            typeof(Retrying),
            typeof(CompactionApplied),
            typeof(StepLimitReached),
            typeof(ToolProgressUpdate),
        ];

    /// <summary>Returns the one-line JSON object for an event, including the stamped fields.</summary>
    internal static string Serialize(AgentEvent agentEvent)
    {
        ArgumentNullException.ThrowIfNull(agentEvent);
        (string type, JsonObject body) = Describe(agentEvent);
        var line = new JsonObject { ["type"] = type, ["runId"] = agentEvent.RunId };
        if (agentEvent.SessionId is not null)
        {
            line["sessionId"] = agentEvent.SessionId;
        }

        if (agentEvent.ParentRunId is not null)
        {
            line["parentRunId"] = agentEvent.ParentRunId;
        }

        if (agentEvent.Source != "core")
        {
            line["source"] = agentEvent.Source;
        }

        foreach ((string name, JsonNode? value) in body)
        {
            line[name] = value?.DeepClone();
        }

        return line.ToJsonString(Options);
    }

    private static (string Type, JsonObject Body) Describe(AgentEvent agentEvent) =>
        agentEvent switch
        {
            RunStarted => ("run_started", new JsonObject()),
            RunFinished e => ("run_finished", new JsonObject { ["stopReason"] = e.StopReason }),
            RunError e => ("run_error", new JsonObject { ["message"] = e.Message }),
            TextMessageStart e => ("text_message_start", MessageBody(e.MessageId)),
            TextMessageContent e => (
                "text_message_content",
                new JsonObject { ["messageId"] = e.MessageId, ["text"] = e.Text }
            ),
            TextMessageEnd e => ("text_message_end", MessageBody(e.MessageId)),
            ToolCallStart e => (
                "tool_call_start",
                ToolCallBody(e.CallId, e.ParentToolCallId, toolName: e.ToolName)
            ),
            ToolCallArgs e => (
                "tool_call_args",
                ToolCallBody(e.CallId, e.ParentToolCallId, args: e.Args)
            ),
            ToolCallEnd e => ("tool_call_end", ToolCallBody(e.CallId, e.ParentToolCallId)),
            ToolCallResult e => ("tool_call_result", ToolResultBody(e)),
            ApprovalRequested e => (
                "approval_requested",
                new JsonObject { ["callId"] = e.CallId, ["toolName"] = e.ToolName, ["args"] = e.Args }
            ),
            UsageUpdated e => (
                "usage_updated",
                new JsonObject { ["usage"] = JsonSerializer.SerializeToNode(e.Usage, Options) }
            ),
            Retrying e => (
                "retrying",
                new JsonObject { ["attempt"] = e.Attempt, ["reason"] = e.Reason }
            ),
            CompactionApplied e => ("compaction_applied", CompactionBody(e)),
            StepLimitReached e => (
                "step_limit_reached",
                new JsonObject { ["maxSteps"] = e.MaxSteps }
            ),
            ToolProgressUpdate e => (
                "tool_progress_update",
                new JsonObject { ["callId"] = e.CallId, ["message"] = e.Message }
            ),
            _ => throw new ArgumentException(
                $"Event type '{agentEvent.GetType().Name}' has no JSON mapping.",
                nameof(agentEvent)
            ),
        };

    private static JsonObject MessageBody(string messageId) =>
        new() { ["messageId"] = messageId };

    private static JsonObject ToolCallBody(
        string callId,
        string? parentToolCallId,
        string? toolName = null,
        string? args = null
    )
    {
        var body = new JsonObject { ["callId"] = callId };
        AddParent(body, parentToolCallId);
        if (toolName is not null)
        {
            body["toolName"] = toolName;
        }

        if (args is not null)
        {
            body["args"] = args;
        }

        return body;
    }

    private static JsonObject ToolResultBody(ToolCallResult e)
    {
        var body = new JsonObject { ["callId"] = e.CallId };
        AddParent(body, e.ParentToolCallId);
        body["output"] = e.Output;
        body["isError"] = e.IsError;
        if (e.Details is not null)
        {
            body["details"] = JsonSerializer.SerializeToNode(
                e.Details,
                e.Details.GetType(),
                Options
            );
        }

        return body;
    }

    private static void AddParent(JsonObject body, string? parentToolCallId)
    {
        if (parentToolCallId is not null)
        {
            body["parentToolCallId"] = parentToolCallId;
        }
    }

    private static JsonObject CompactionBody(CompactionApplied e)
    {
        var replaced = new JsonArray();
        foreach (string id in e.ReplacedEntryIds)
        {
            replaced.Add(id);
        }

        return new JsonObject
        {
            ["replacedEntryIds"] = replaced,
            ["estimatedTokensAfter"] = e.EstimatedTokensAfter,
        };
    }
}

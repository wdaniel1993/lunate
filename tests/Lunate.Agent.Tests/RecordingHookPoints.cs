using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

internal sealed class RecordingHookPoints : IAgentHookPoints
{
    public List<string> Calls { get; } = [];

    public List<AgentRunStartingContext> RunStartings { get; } = [];

    public List<AgentContextBuildingContext> ContextBuildings { get; } = [];

    public List<(string RunId, ChatResponseUpdate Update)> StreamEvents { get; } = [];

    public List<AgentMessageCompletedContext> MessageCompletions { get; } = [];

    public List<AgentToolCallingContext> ToolCallings { get; } = [];

    public List<AgentToolResultReadyContext> ToolResults { get; } = [];

    public List<AgentTurnEndedContext> TurnEnds { get; } = [];

    public List<AgentRunSettledContext> Settles { get; } = [];

    public List<AgentCompactingContext> Compactings { get; } = [];

    public Func<AgentRunStartingContext, AgentRunStartingResult> RunStarting { get; set; } =
        static _ => new AgentRunStartingResult.None();

    public Func<
        AgentContextBuildingContext,
        AgentContextBuildingResult
    > ContextBuilding { get; set; } = static _ => AgentContextBuildingResult.None;

    public Func<
        AgentMessageCompletedContext,
        AgentMessageCompletedResult
    > MessageCompleted { get; set; } = static _ => new AgentMessageCompletedResult.Keep();

    public Func<AgentToolCallingContext, AgentToolCallingResult> ToolCalling { get; set; } =
        static context => new AgentToolCallingResult.Proceed(context.Arguments);

    public Func<
        AgentToolResultReadyContext,
        AgentToolResultReadyResult
    > ToolResultReady { get; set; } =
        static context => new AgentToolResultReadyResult(context.Output, null);

    public Func<AgentTurnEndedContext, AgentTurnEndedResult> TurnEnded { get; set; } =
        static _ => AgentTurnEndedResult.None;

    public Func<AgentCompactingContext, AgentCompactingResult> Compacting { get; set; } =
        static _ => new AgentCompactingResult.UseDefault();

    public ValueTask<AgentRunStartingResult> RunStartingAsync(
        AgentRunStartingContext context,
        CancellationToken cancellationToken
    )
    {
        Calls.Add("RunStarting");
        RunStartings.Add(context);
        return ValueTask.FromResult(RunStarting(context));
    }

    public ValueTask<AgentContextBuildingResult> ContextBuildingAsync(
        AgentContextBuildingContext context,
        CancellationToken cancellationToken
    )
    {
        Calls.Add("ContextBuilding");
        ContextBuildings.Add(context);
        return ValueTask.FromResult(ContextBuilding(context));
    }

    public ValueTask ProviderStreamEventAsync(
        string runId,
        ChatResponseUpdate update,
        CancellationToken cancellationToken
    )
    {
        Calls.Add("ProviderStreamEvent");
        StreamEvents.Add((runId, update));
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentMessageCompletedResult> MessageCompletedAsync(
        AgentMessageCompletedContext context,
        CancellationToken cancellationToken
    )
    {
        Calls.Add("MessageCompleted");
        MessageCompletions.Add(context);
        return ValueTask.FromResult(MessageCompleted(context));
    }

    public ValueTask<AgentToolCallingResult> ToolCallingAsync(
        AgentToolCallingContext context,
        CancellationToken cancellationToken
    )
    {
        Calls.Add("ToolCalling");
        ToolCallings.Add(context);
        return ValueTask.FromResult(ToolCalling(context));
    }

    public ValueTask<AgentToolResultReadyResult> ToolResultReadyAsync(
        AgentToolResultReadyContext context,
        CancellationToken cancellationToken
    )
    {
        Calls.Add("ToolResultReady");
        ToolResults.Add(context);
        return ValueTask.FromResult(ToolResultReady(context));
    }

    public ValueTask<AgentTurnEndedResult> TurnEndedAsync(
        AgentTurnEndedContext context,
        CancellationToken cancellationToken
    )
    {
        Calls.Add("TurnEnded");
        TurnEnds.Add(context);
        return ValueTask.FromResult(TurnEnded(context));
    }

    public ValueTask RunSettledAsync(
        AgentRunSettledContext context,
        CancellationToken cancellationToken
    )
    {
        Calls.Add("RunSettled");
        Settles.Add(context);
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentCompactingResult> CompactingAsync(
        AgentCompactingContext context,
        CancellationToken cancellationToken
    )
    {
        Calls.Add("Compacting");
        Compactings.Add(context);
        return ValueTask.FromResult(Compacting(context));
    }
}

internal sealed class EmptyHookPoints : IAgentHookPoints { }

internal static class AgentHookTestSupport
{
    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static ChatResponseUpdate TextUpdate(string text, ChatFinishReason? finish = null) =>
        new(ChatRole.Assistant, [new TextContent(text)]) { FinishReason = finish };
}

using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// The harness's extension seam. Every member is a no-op default, so a harness configured without
/// hooks behaves exactly as one without the seam. Implementations never throw: a failing hook is
/// the hook layer's responsibility to contain.
/// </summary>
public interface IAgentHookPoints
{
    /// <summary>Called before the run's first model call; edits prompt sections and selects tools.</summary>
    ValueTask<AgentRunStartingResult> RunStartingAsync(
        AgentRunStartingContext context,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<AgentRunStartingResult>(new AgentRunStartingResult.None());

    /// <summary>Called before every model request; adds request-local messages.</summary>
    ValueTask<AgentContextBuildingResult> ContextBuildingAsync(
        AgentContextBuildingContext context,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(AgentContextBuildingResult.None);

    /// <summary>Called for every raw provider update; observation only.</summary>
    ValueTask ProviderStreamEventAsync(
        string runId,
        ChatResponseUpdate update,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;

    /// <summary>Called when the final assistant message of a model call is known.</summary>
    ValueTask<AgentMessageCompletedResult> MessageCompletedAsync(
        AgentMessageCompletedContext context,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<AgentMessageCompletedResult>(new AgentMessageCompletedResult.Keep());

    /// <summary>Called before approval; the returned arguments are the ones approved and executed.</summary>
    ValueTask<AgentToolCallingResult> ToolCallingAsync(
        AgentToolCallingContext context,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<AgentToolCallingResult>(
            new AgentToolCallingResult.Proceed(context.Arguments)
        );

    /// <summary>Called after execution; transforms the text and may attach JSON data.</summary>
    ValueTask<AgentToolResultReadyResult> ToolResultReadyAsync(
        AgentToolResultReadyContext context,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(new AgentToolResultReadyResult(context.Output, null));

    /// <summary>Called at a turn boundary; entries are persisted and one continuation may be requested.</summary>
    ValueTask<AgentTurnEndedResult> TurnEndedAsync(
        AgentTurnEndedContext context,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(AgentTurnEndedResult.None);

    /// <summary>Called once the run has settled; observation only.</summary>
    ValueTask RunSettledAsync(
        AgentRunSettledContext context,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;
}

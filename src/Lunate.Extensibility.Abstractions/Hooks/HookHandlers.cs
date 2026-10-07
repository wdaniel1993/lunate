namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// Base contract of every hook handler. <see cref="Priority"/> orders handlers: higher values run
/// first, and equal priorities keep extension load order.
/// </summary>
public interface IHookHandler
{
    /// <summary>Higher values run earlier; equal priorities keep extension load order.</summary>
    int Priority { get; }
}

/// <summary>Decides whether an untrusted project extension may load.</summary>
public interface IProjectTrustHandler : IHookHandler
{
    ValueTask<ProjectTrustResult> HandleAsync(
        ProjectTrustPayload payload,
        CancellationToken cancellationToken
    );
}

/// <summary>Starts long-lived resources once a session is live; safe to run more than once.</summary>
public interface ISessionStartedHandler : IHookHandler
{
    ValueTask HandleAsync(SessionStartedPayload payload, CancellationToken cancellationToken);
}

/// <summary>Stops resources; must be safe to run more than once.</summary>
public interface ISessionEndingHandler : IHookHandler
{
    ValueTask HandleAsync(SessionEndingPayload payload, CancellationToken cancellationToken);
}

/// <summary>
/// Observes raw user input. Contract-complete but unwired in this change; the input layer/TUI
/// will produce it.
/// </summary>
public interface IInputReceivedHandler : IHookHandler
{
    ValueTask<InputReceivedResult> HandleAsync(
        InputReceivedPayload payload,
        CancellationToken cancellationToken
    );
}

/// <summary>Edits prompt sections and selects active tools before a run's first model call.</summary>
public interface IRunStartingHandler : IHookHandler
{
    ValueTask<RunStartingResult> HandleAsync(
        RunStartingPayload payload,
        CancellationToken cancellationToken
    );
}

/// <summary>Adds request-local context before every model call.</summary>
public interface IContextBuildingHandler : IHookHandler
{
    ValueTask<ContextBuildingResult> HandleAsync(
        ContextBuildingPayload payload,
        CancellationToken cancellationToken
    );
}

/// <summary>Observes every raw provider update; results are ignored.</summary>
public interface IProviderStreamEventHandler : IHookHandler
{
    ValueTask ObserveAsync(ProviderStreamEventPayload payload, CancellationToken cancellationToken);
}

/// <summary>Keeps or replaces the final assistant message of a model call.</summary>
public interface IMessageCompletedHandler : IHookHandler
{
    ValueTask<MessageCompletedResult> HandleAsync(
        MessageCompletedPayload payload,
        CancellationToken cancellationToken
    );
}

/// <summary>Mutates tool arguments or blocks the call before approval and execution.</summary>
public interface IToolCallingHandler : IHookHandler
{
    ValueTask<ToolCallingResult> HandleAsync(
        ToolCallingPayload payload,
        CancellationToken cancellationToken
    );
}

/// <summary>Transforms a tool result and attaches JSON data after execution.</summary>
public interface IToolResultReadyHandler : IHookHandler
{
    ValueTask<ToolResultReadyResult> HandleAsync(
        ToolResultReadyPayload payload,
        CancellationToken cancellationToken
    );
}

/// <summary>Appends session entries and may request one continuation at a turn boundary.</summary>
public interface ITurnEndedHandler : IHookHandler
{
    ValueTask<TurnEndedResult> HandleAsync(
        TurnEndedPayload payload,
        CancellationToken cancellationToken
    );
}

/// <summary>Observes the settled run; results are ignored.</summary>
public interface IRunSettledHandler : IHookHandler
{
    ValueTask ObserveAsync(RunSettledPayload payload, CancellationToken cancellationToken);
}

/// <summary>
/// Supplies a compaction summary. Contract-complete but unwired in this change; the compaction
/// layer will produce it.
/// </summary>
public interface ICompactingHandler : IHookHandler
{
    ValueTask<CompactingResult> HandleAsync(
        CompactingPayload payload,
        CancellationToken cancellationToken
    );
}

/// <summary>Observes a model change; results are ignored.</summary>
public interface IModelChangedHandler : IHookHandler
{
    ValueTask ObserveAsync(ModelChangedPayload payload, CancellationToken cancellationToken);
}

/// <summary>Observes a tool-set change; results are ignored.</summary>
public interface IToolsChangedHandler : IHookHandler
{
    ValueTask ObserveAsync(ToolsChangedPayload payload, CancellationToken cancellationToken);
}

/// <summary>Observes file changes the core emits after successful <c>write</c>/<c>edit</c> mutations.</summary>
public interface IFileChangedHandler : IHookHandler
{
    ValueTask OnFileChangedAsync(
        FileChangedPayload payload,
        CancellationToken cancellationToken
    );
}

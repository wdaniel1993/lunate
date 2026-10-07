using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

internal sealed class TestProjectTrustHandler(
    Func<ProjectTrustPayload, CancellationToken, ValueTask<ProjectTrustResult>> handle
) : IProjectTrustHandler
{
    public int Priority { get; init; }

    public ValueTask<ProjectTrustResult> HandleAsync(
        ProjectTrustPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal sealed class TestSessionStartedHandler(
    Func<SessionStartedPayload, CancellationToken, ValueTask> handle
) : ISessionStartedHandler
{
    public int Priority { get; init; }

    public ValueTask HandleAsync(
        SessionStartedPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal sealed class TestSessionEndingHandler(
    Func<SessionEndingPayload, CancellationToken, ValueTask> handle
) : ISessionEndingHandler
{
    public int Priority { get; init; }

    public ValueTask HandleAsync(
        SessionEndingPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal sealed class TestInputReceivedHandler(
    Func<InputReceivedPayload, CancellationToken, ValueTask<InputReceivedResult>> handle
) : IInputReceivedHandler
{
    public int Priority { get; init; }

    public ValueTask<InputReceivedResult> HandleAsync(
        InputReceivedPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal sealed class TestRunStartingHandler(
    Func<RunStartingPayload, CancellationToken, ValueTask<RunStartingResult>> handle
) : IRunStartingHandler
{
    public int Priority { get; init; }

    public ValueTask<RunStartingResult> HandleAsync(
        RunStartingPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal sealed class TestContextBuildingHandler(
    Func<ContextBuildingPayload, CancellationToken, ValueTask<ContextBuildingResult>> handle
) : IContextBuildingHandler
{
    public int Priority { get; init; }

    public ValueTask<ContextBuildingResult> HandleAsync(
        ContextBuildingPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal sealed class TestProviderStreamEventHandler(
    Func<ProviderStreamEventPayload, CancellationToken, ValueTask> handle
) : IProviderStreamEventHandler
{
    public int Priority { get; init; }

    public ValueTask ObserveAsync(
        ProviderStreamEventPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal sealed class TestMessageCompletedHandler(
    Func<MessageCompletedPayload, CancellationToken, ValueTask<MessageCompletedResult>> handle
) : IMessageCompletedHandler
{
    public int Priority { get; init; }

    public ValueTask<MessageCompletedResult> HandleAsync(
        MessageCompletedPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal sealed class TestToolCallingHandler(
    Func<ToolCallingPayload, CancellationToken, ValueTask<ToolCallingResult>> handle
) : IToolCallingHandler
{
    public int Priority { get; init; }

    public ValueTask<ToolCallingResult> HandleAsync(
        ToolCallingPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal sealed class TestToolResultReadyHandler(
    Func<ToolResultReadyPayload, CancellationToken, ValueTask<ToolResultReadyResult>> handle
) : IToolResultReadyHandler
{
    public int Priority { get; init; }

    public ValueTask<ToolResultReadyResult> HandleAsync(
        ToolResultReadyPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal sealed class TestTurnEndedHandler(
    Func<TurnEndedPayload, CancellationToken, ValueTask<TurnEndedResult>> handle
) : ITurnEndedHandler
{
    public int Priority { get; init; }

    public ValueTask<TurnEndedResult> HandleAsync(
        TurnEndedPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal sealed class TestRunSettledHandler(
    Func<RunSettledPayload, CancellationToken, ValueTask> handle
) : IRunSettledHandler
{
    public int Priority { get; init; }

    public ValueTask ObserveAsync(RunSettledPayload payload, CancellationToken cancellationToken) =>
        handle(payload, cancellationToken);
}

internal sealed class TestCompactingHandler(
    Func<CompactingPayload, CancellationToken, ValueTask<CompactingResult>> handle
) : ICompactingHandler
{
    public int Priority { get; init; }

    public ValueTask<CompactingResult> HandleAsync(
        CompactingPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal sealed class TestModelChangedHandler(
    Func<ModelChangedPayload, CancellationToken, ValueTask> handle
) : IModelChangedHandler
{
    public int Priority { get; init; }

    public ValueTask ObserveAsync(
        ModelChangedPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal sealed class TestToolsChangedHandler(
    Func<ToolsChangedPayload, CancellationToken, ValueTask> handle
) : IToolsChangedHandler
{
    public int Priority { get; init; }

    public ValueTask ObserveAsync(
        ToolsChangedPayload payload,
        CancellationToken cancellationToken
    ) => handle(payload, cancellationToken);
}

internal static class TestHookPayloads
{
    public static SessionStartedPayload SessionStarted => new("/work", "repo");

    public static SessionEndingPayload SessionEnding => new("/work", "repo");

    public static InputReceivedPayload Input => new("hello");

    public static RunStartingPayload RunStarting =>
        new("r1", [new PromptSection("system", "base")], ["read"]);

    public static ContextBuildingPayload ContextBuilding => new("r1", []);

    public static ProviderStreamEventPayload StreamEvent => new("r1", "model", "text", null);

    public static MessageCompletedPayload MessageCompleted => new("r1", "assistant", "draft");

    public static ToolCallingPayload ToolCalling =>
        new("r1", "c1", "read", System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone());

    public static ToolResultReadyPayload ToolResultReady =>
        new("r1", "c1", "read", "original", false);

    public static TurnEndedPayload TurnEnded => new("r1");

    public static RunSettledPayload RunSettled => new("r1");

    public static CompactingPayload Compacting => new("r1", []);

    public static ModelChangedPayload ModelChanged => new("r1", "model-2");

    public static ToolsChangedPayload ToolsChanged => new("r1", ["read"]);
}

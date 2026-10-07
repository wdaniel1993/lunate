using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed partial class HookRunner
{
    /// <summary>Runs <see cref="ISessionStartedHandler"/> handlers; failures are reported.</summary>
    public ValueTask RunSessionStartedAsync(
        SessionStartedPayload payload,
        CancellationToken cancellationToken = default
    ) =>
        ObserveAsync<ISessionStartedHandler>(
            "SessionStarted",
            (handler, ct) => handler.HandleAsync(payload, ct),
            _options.HandlerTimeout,
            cancellationToken
        );

    /// <summary>Runs <see cref="ISessionEndingHandler"/> handlers; failures are reported.</summary>
    public ValueTask RunSessionEndingAsync(
        SessionEndingPayload payload,
        CancellationToken cancellationToken = default
    ) =>
        ObserveAsync<ISessionEndingHandler>(
            "SessionEnding",
            (handler, ct) => handler.HandleAsync(payload, ct),
            _options.HandlerTimeout,
            cancellationToken
        );

    /// <summary>Runs <see cref="IProviderStreamEventHandler"/> handlers on the fast path.</summary>
    public ValueTask RunProviderStreamEventAsync(
        ProviderStreamEventPayload payload,
        CancellationToken cancellationToken = default
    ) =>
        ObserveAsync<IProviderStreamEventHandler>(
            "ProviderStreamEvent",
            (handler, ct) => handler.ObserveAsync(payload, ct),
            _options.ProviderStreamEventTimeout,
            cancellationToken
        );

    /// <summary>Runs <see cref="IRunSettledHandler"/> handlers and clears the run's continuation count.</summary>
    public ValueTask RunRunSettledAsync(
        RunSettledPayload payload,
        CancellationToken cancellationToken = default
    )
    {
        _continuations.Remove(payload.RunId);
        return ObserveAsync<IRunSettledHandler>(
            "RunSettled",
            (handler, ct) => handler.ObserveAsync(payload, ct),
            _options.HandlerTimeout,
            cancellationToken
        );
    }

    /// <summary>Runs <see cref="IModelChangedHandler"/> handlers; failures are reported.</summary>
    public ValueTask RunModelChangedAsync(
        ModelChangedPayload payload,
        CancellationToken cancellationToken = default
    ) =>
        ObserveAsync<IModelChangedHandler>(
            "ModelChanged",
            (handler, ct) => handler.ObserveAsync(payload, ct),
            _options.HandlerTimeout,
            cancellationToken
        );

    /// <summary>Runs <see cref="IToolsChangedHandler"/> handlers; failures are reported.</summary>
    public ValueTask RunToolsChangedAsync(
        ToolsChangedPayload payload,
        CancellationToken cancellationToken = default
    ) =>
        ObserveAsync<IToolsChangedHandler>(
            "ToolsChanged",
            (handler, ct) => handler.ObserveAsync(payload, ct),
            _options.HandlerTimeout,
            cancellationToken
        );

    private async ValueTask ObserveAsync<THandler>(
        string hook,
        Func<THandler, CancellationToken, ValueTask> call,
        TimeSpan timeout,
        CancellationToken ct
    )
        where THandler : IHookHandler
    {
        foreach (Registration registration in Ordered<THandler>())
        {
            var handler = (THandler)registration.Handler;
            HandlerOutcome<bool> outcome = await CallVoidAsync(
                    token => call(handler, token),
                    timeout,
                    ct
                )
                .ConfigureAwait(false);
            if (outcome.Status is not HandlerStatus.Completed)
            {
                LogFailure(hook, registration, timeout, outcome.Error);
            }
        }
    }
}

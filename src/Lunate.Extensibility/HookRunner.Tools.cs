using System.Globalization;
using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed partial class HookRunner
{
    /// <summary>
    /// Runs <see cref="IProjectTrustHandler"/> handlers (optionally restricted to given extension
    /// ids); the first deny wins and a handler failure denies fail-safe.
    /// </summary>
    public async ValueTask<ProjectTrustDispatch> RunProjectTrustAsync(
        ProjectTrustPayload payload,
        IReadOnlyCollection<string>? extensionIds = null,
        CancellationToken cancellationToken = default
    )
    {
        int count = 0;
        foreach (Registration registration in Ordered<IProjectTrustHandler>(extensionIds))
        {
            count++;
            var handler = (IProjectTrustHandler)registration.Handler;
            HandlerOutcome<ProjectTrustResult> outcome = await CallAsync(
                    token => handler.HandleAsync(payload, token),
                    _options.HandlerTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (outcome.Status is not HandlerStatus.Completed)
            {
                LogFailure("ProjectTrust", registration, _options.HandlerTimeout, outcome.Error);
                return new ProjectTrustDispatch(
                    count,
                    new ProjectTrustResult.Deny(
                        $"the ProjectTrust handler from extension '{registration.ExtensionId}' failed; the project extension was not trusted."
                    )
                );
            }

            if (outcome.Value is ProjectTrustResult.Deny deny)
            {
                return new ProjectTrustDispatch(count, deny);
            }
        }

        return new ProjectTrustDispatch(count, count == 0 ? null : new ProjectTrustResult.Allow());
    }

    /// <summary>
    /// Runs <see cref="IToolCallingHandler"/> handlers; each sees the current arguments, the first
    /// block wins, and a handler failure blocks fail-safe.
    /// </summary>
    public async ValueTask<ToolCallingResult> RunToolCallingAsync(
        ToolCallingPayload payload,
        CancellationToken cancellationToken = default
    )
    {
        JsonElement arguments = payload.Arguments;
        foreach (Registration registration in Ordered<IToolCallingHandler>())
        {
            var handler = (IToolCallingHandler)registration.Handler;
            HandlerOutcome<ToolCallingResult> outcome = await CallAsync(
                    token => handler.HandleAsync(payload with { Arguments = arguments }, token),
                    _options.HandlerTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (outcome.Status is not HandlerStatus.Completed)
            {
                LogFailure("ToolCalling", registration, _options.HandlerTimeout, outcome.Error);
                return new ToolCallingResult.Block(
                    $"the ToolCalling handler from extension '{registration.ExtensionId}' failed; the tool call was blocked."
                );
            }

            switch (outcome.Value)
            {
                case ToolCallingResult.Proceed proceed when proceed.Arguments is { } mutated:
                    arguments = mutated;
                    break;
                case ToolCallingResult.Block block:
                    return block;
            }
        }

        return new ToolCallingResult.Proceed(arguments);
    }

    /// <summary>
    /// Runs <see cref="ITurnEndedHandler"/> handlers, stamps their entries with the owning extension
    /// and caps continuations per run.
    /// </summary>
    public async ValueTask<TurnEndedDispatch> RunTurnEndedAsync(
        TurnEndedPayload payload,
        CancellationToken cancellationToken = default
    )
    {
        var entries = new List<TurnEndedEntry>();
        bool requested = false;
        foreach (Registration registration in Ordered<ITurnEndedHandler>())
        {
            var handler = (ITurnEndedHandler)registration.Handler;
            HandlerOutcome<TurnEndedResult> outcome = await CallAsync(
                    token => handler.HandleAsync(payload, token),
                    _options.HandlerTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (outcome.Status is not HandlerStatus.Completed)
            {
                LogFailure("TurnEnded", registration, _options.HandlerTimeout, outcome.Error);
                continue;
            }

            if (outcome.Value is TurnEndedResult.TurnEnded ended)
            {
                entries.AddRange(
                    ended.Entries.Select(entry =>
                        entry with
                        {
                            ExtensionId = registration.ExtensionId,
                        }
                    )
                );
                requested |= ended.RequestContinuation;
            }
        }

        if (requested)
        {
            int count = _continuations.GetValueOrDefault(payload.RunId);
            if (count >= _options.MaxContinuations)
            {
                _log.Warn(
                    $"hook TurnEnded: the continuation request for run '{payload.RunId}' was ignored: the per-run cap of {_options.MaxContinuations.ToString(CultureInfo.InvariantCulture)} continuations is reached."
                );
                requested = false;
            }
            else
            {
                _continuations[payload.RunId] = count + 1;
            }
        }

        return new TurnEndedDispatch(entries, requested);
    }

    /// <summary>
    /// Runs <see cref="ICompactingHandler"/> handlers; the last supplied summary wins and a handler
    /// failure falls back to the host default.
    /// </summary>
    public async ValueTask<CompactingResult> RunCompactingAsync(
        CompactingPayload payload,
        CancellationToken cancellationToken = default
    )
    {
        string? summary = null;
        foreach (Registration registration in Ordered<ICompactingHandler>())
        {
            var handler = (ICompactingHandler)registration.Handler;
            HandlerOutcome<CompactingResult> outcome = await CallAsync(
                    token => handler.HandleAsync(payload, token),
                    _options.HandlerTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (outcome.Status is not HandlerStatus.Completed)
            {
                LogFailure("Compacting", registration, _options.HandlerTimeout, outcome.Error);
                return new CompactingResult.UseDefault();
            }

            if (outcome.Value is CompactingResult.Provide provide)
            {
                summary = provide.Summary;
            }
        }

        return summary is null
            ? new CompactingResult.UseDefault()
            : new CompactingResult.Provide(summary);
    }
}

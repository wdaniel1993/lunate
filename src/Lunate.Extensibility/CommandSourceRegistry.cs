using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The command sources extensions registered, in registration order. Registration is registration
/// only; descriptions are cached by the loader at load time (the manifest cross-check asks once),
/// and every registration leaves with its extension on unload. Each registration hands out a
/// <see cref="LeasedCommandSource"/>, so an invocation always holds a lease on its extension and an
/// unload can cancel and drain it before the load context goes away.
/// </summary>
public sealed class CommandSourceRegistry
{
    private readonly IExtensionLog _log;
    private readonly Action<string>? _notify;
    private readonly CommandInvocationLeases _leases;
    private readonly object _gate = new();
    private readonly List<Registration> _registrations = [];

    /// <param name="log">Where diagnostics go; null logs nothing.</param>
    /// <param name="notify">
    /// The one-line user notice sink (an unload cancelling a live command); null notifies nobody.
    /// </param>
    public CommandSourceRegistry(IExtensionLog? log = null, Action<string>? notify = null)
    {
        _log = log ?? NullExtensionLog.Instance;
        _notify = notify;
        _leases = new CommandInvocationLeases(_log);
    }

    public void Register(string extensionId, ICommandSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceId);
        var leased = new LeasedCommandSource(extensionId, source, _leases);
        lock (_gate)
        {
            if (
                _registrations.Any(registration =>
                    string.Equals(registration.ExtensionId, extensionId, StringComparison.Ordinal)
                    && string.Equals(
                        registration.Source.SourceId,
                        source.SourceId,
                        StringComparison.Ordinal
                    )
                )
            )
            {
                _log.Warn(
                    $"commands: extension '{extensionId}' registered source '{source.SourceId}' more than once; every registration contributes."
                );
            }

            _registrations.Add(new Registration(extensionId, source, leased));
        }

        // A reload of the same id starts a fresh lifetime: the previous unload left the leases
        // cancelled so a stale caller could not start a handler in the old context.
        _leases.Reset(extensionId);
    }

    /// <summary>The registered sources (wrapped in their lease) and cached descriptions, in order.</summary>
    public IReadOnlyList<RegisteredCommandSource> List()
    {
        lock (_gate)
        {
            return
            [
                .. _registrations.Select(registration => new RegisteredCommandSource(
                    registration.ExtensionId,
                    registration.Leased,
                    registration.Descriptors
                )),
            ];
        }
    }

    /// <summary>
    /// Stores the descriptors the loader cross-checked against the manifest. The match is the
    /// registration's extension id plus the inner source, never the wrapper the caller sees: a
    /// wrapper in the middle would make object identity match nothing, and the stamped descriptors
    /// would silently vanish (design D3, implementation note).
    /// </summary>
    internal void SetDescriptors(
        ICommandSource source,
        IReadOnlyList<CommandDescriptor> descriptors
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(descriptors);
        ICommandSource inner = source is LeasedCommandSource lease ? lease.Inner : source;
        string? extensionId = source is LeasedCommandSource leased ? leased.ExtensionId : null;
        lock (_gate)
        {
            for (var index = 0; index < _registrations.Count; index++)
            {
                Registration registration = _registrations[index];
                if (!ReferenceEquals(registration.Source, inner))
                {
                    continue;
                }

                if (
                    extensionId is not null
                    && !string.Equals(
                        registration.ExtensionId,
                        extensionId,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                _registrations[index] = registration with { Descriptors = descriptors };
            }
        }
    }

    /// <summary>
    /// Removes every source the extension registered. Synchronous on purpose: the three load-failure
    /// cleanup sites call it while the extension is still being created, where no invocation can be
    /// in flight. An unload uses <see cref="CancelInFlightAsync"/> instead.
    /// </summary>
    public void Unregister(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        lock (_gate)
        {
            _registrations.RemoveAll(registration =>
                string.Equals(registration.ExtensionId, extensionId, StringComparison.Ordinal)
            );
        }
    }

    /// <summary>
    /// Cancels every live invocation of the extension, tells the user (one notice per command id),
    /// and waits a bounded time for the handlers to return; false means the bound expired and the
    /// handlers are still running (design D3, sign-off Q2).
    /// </summary>
    internal ValueTask<bool> CancelInFlightAsync(
        string extensionId,
        TimeSpan bound,
        CancellationToken cancellationToken
    ) => CancelInFlightAsync(extensionId, bound, notify: true, cancellationToken);

    /// <summary>
    /// The same, with the notice suppressed: the session-quit path cancels in flight commands
    /// silently because no user is looking at the session any more (sign-off Q2).
    /// </summary>
    internal async ValueTask<bool> CancelInFlightAsync(
        string extensionId,
        TimeSpan bound,
        bool notify,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        IReadOnlyList<string> cancelled = _leases.Cancel(extensionId);
        if (notify)
        {
            foreach (string commandId in cancelled)
            {
                _notify?.Invoke(
                    FormattableString.Invariant(
                        $"command {commandId} was cancelled because extension '{extensionId}' was unloaded"
                    )
                );
            }
        }

        bool drained = await _leases
            .DrainAsync(extensionId, bound, cancellationToken)
            .ConfigureAwait(false);
        if (!drained && cancelled.Count > 0)
        {
            // Once per unload: a second caller while the handler is still stuck reports nothing.
            _log.Warn(
                FormattableString.Invariant(
                    $"command {string.Join(", ", _leases.InFlight(extensionId))} did not return within {bound.TotalMilliseconds:0} ms of the unload; its result is discarded and extension '{extensionId}' unloads anyway."
                )
            );
        }

        return drained;
    }

    private sealed record Registration(
        string ExtensionId,
        ICommandSource Source,
        LeasedCommandSource Leased
    )
    {
        public IReadOnlyList<CommandDescriptor> Descriptors { get; init; } = [];
    }
}

/// <summary>One registered command source, its owning extension and the load-time descriptors.</summary>
public sealed record RegisteredCommandSource(
    string ExtensionId,
    ICommandSource Source,
    IReadOnlyList<CommandDescriptor> Descriptors
);

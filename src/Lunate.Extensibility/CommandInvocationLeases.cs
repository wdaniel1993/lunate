using System.Globalization;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The in-flight command invocations of every loaded extension (design D3, sign-off Q2). One gate
/// covers a per-extension record, so an unload can never race a handler start into a half-cancelled
/// state: entering after the cancel is refused outright, and the record keeps the command ids it is
/// waiting on so a stuck handler can be logged by name.
/// </summary>
internal sealed class CommandInvocationLeases(IExtensionLog log)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ExtensionLeases> _extensions = new(StringComparer.Ordinal);

    /// <summary>
    /// Enters the lease for one invocation. A refused lease (the extension is unloading or gone)
    /// carries an already-cancelled token, so a caller holding a stale registry snapshot can never
    /// start a handler inside a context that is about to unload.
    /// </summary>
    internal CommandInvocationLease Enter(string extensionId, string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        lock (_gate)
        {
            if (!_extensions.TryGetValue(extensionId, out ExtensionLeases? state))
            {
                state = new ExtensionLeases();
                _extensions[extensionId] = state;
            }

            if (state.Cancelled)
            {
                return CommandInvocationLease.Refused(commandId);
            }

            state.Cancellation ??= new CancellationTokenSource();
            state.Active++;
            state.CommandIds.Add(commandId);
            return new CommandInvocationLease(
                this,
                extensionId,
                commandId,
                state.Cancellation.Token
            );
        }
    }

    /// <summary>The exits one lease and completes the drain when the last one leaves.</summary>
    internal void Exit(string extensionId, string commandId)
    {
        lock (_gate)
        {
            if (!_extensions.TryGetValue(extensionId, out ExtensionLeases? state))
            {
                return;
            }

            state.CommandIds.Remove(commandId);
            state.Active = Math.Max(0, state.Active - 1);
            if (state.Active == 0)
            {
                state.Drained.TrySetResult();
            }
        }
    }

    /// <summary>
    /// Cancels the extension's live invocations and returns their command ids (empty when nothing
    /// is in flight or the extension was already cancelled). The cancel runs outside the gate: a
    /// handler's continuation may run inline and must never be invoked while the lock is held.
    /// </summary>
    internal IReadOnlyList<string> Cancel(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        CancellationTokenSource? cancellation;
        List<string> commandIds;
        lock (_gate)
        {
            if (!_extensions.TryGetValue(extensionId, out ExtensionLeases? state))
            {
                // Nothing is in flight, but the extension is going away: remember the cancel so a
                // caller holding a stale source cannot start a handler in the unloaded context.
                _extensions[extensionId] = new ExtensionLeases { Cancelled = true };
                return [];
            }

            if (state.Cancelled)
            {
                return [];
            }

            state.Cancelled = true;
            commandIds = [.. state.CommandIds];
            cancellation = state.Cancellation;
        }

        cancellation?.Cancel();
        return commandIds;
    }

    /// <summary>
    /// Waits for the cancelled invocations to leave. False when the bound expired first — the
    /// caller reports the stuck handler and unloads anyway.
    /// </summary>
    internal async ValueTask<bool> DrainAsync(
        string extensionId,
        TimeSpan bound,
        CancellationToken cancellationToken
    )
    {
        Task drained;
        lock (_gate)
        {
            if (
                !_extensions.TryGetValue(extensionId, out ExtensionLeases? state)
                || state.Active == 0
            )
            {
                return true;
            }

            drained = state.Drained.Task;
        }

        try
        {
            await drained.WaitAsync(bound, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>The command ids still running for the extension (the unresponsive-handler report).</summary>
    internal IReadOnlyList<string> InFlight(string extensionId)
    {
        lock (_gate)
        {
            return _extensions.TryGetValue(extensionId, out ExtensionLeases? state)
                ? [.. state.CommandIds]
                : [];
        }
    }

    /// <summary>
    /// Forgets a cancelled extension's record, so a reload of the same id starts a fresh lifetime.
    /// A live record is never touched (registration happens before any invocation can exist).
    /// </summary>
    internal void Reset(string extensionId)
    {
        ExtensionLeases? removed;
        lock (_gate)
        {
            _extensions.Remove(extensionId, out removed);
            if (removed is { Cancelled: false })
            {
                // A live record is never touched: registration happens before any invocation can
                // exist, so only a leftover cancelled record is ever removed here.
                _extensions[extensionId] = removed;
            }
        }

        if (removed is { Cancelled: true })
        {
            removed.Cancellation?.Dispose();
        }
    }

    /// <summary>Whether the extension was cancelled (its live invocations must not render).</summary>
    internal bool IsCancelled(string extensionId)
    {
        lock (_gate)
        {
            return !_extensions.TryGetValue(extensionId, out ExtensionLeases? state)
                || state.Cancelled;
        }
    }

    /// <summary>
    /// A host call that outlived its extension: contained here, logged once with the command id,
    /// never surfaced as a crash or a stack trace (sign-off Q2).
    /// </summary>
    internal void ReportLateHostCall(string commandId, string extensionId) =>
        log.Warn(
            FormattableString.Invariant(
                $"command {commandId}: extension '{extensionId}' was unloaded while the handler ran; a host call after the unload threw {nameof(ObjectDisposedException)} and the result was discarded."
            )
        );

    private sealed class ExtensionLeases
    {
        public bool Cancelled { get; set; }

        public int Active { get; set; }

        public CancellationTokenSource? Cancellation { get; set; }

        public List<string> CommandIds { get; } = [];

        public TaskCompletionSource Drained { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

/// <summary>
/// One command invocation's hold on its extension (design D3). Disposing it is the exit the drain
/// waits for; <see cref="Cancelled"/> tells the caller that an unload ended the invocation, so its
/// result must be discarded rather than rendered.
/// </summary>
internal sealed class CommandInvocationLease : IDisposable
{
    private readonly CommandInvocationLeases? _leases;
    private readonly string _extensionId;
    private readonly string _commandId;
    private bool _disposed;

    internal CommandInvocationLease(
        CommandInvocationLeases leases,
        string extensionId,
        string commandId,
        CancellationToken token
    )
    {
        _leases = leases;
        _extensionId = extensionId;
        _commandId = commandId;
        Token = token;
    }

    private CommandInvocationLease(string commandId)
    {
        _extensionId = string.Empty;
        _commandId = commandId;
        Token = new CancellationToken(canceled: true);
    }

    internal static CommandInvocationLease Refused(string commandId) => new(commandId);

    /// <summary>The token the handler runs under: the caller's, linked with the unload's cancel.</summary>
    internal CancellationToken Token { get; }

    /// <summary>Whether an unload (or an already-unloaded extension) ended this invocation.</summary>
    internal bool Cancelled => _leases is null || _leases.IsCancelled(_extensionId);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _leases?.Exit(_extensionId, _commandId);
    }
}

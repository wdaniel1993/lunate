using System.Threading.Channels;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The in-process file change bus: the core emits one <see cref="FileChangedPayload"/> per
/// successful queued mutation (a <see cref="FileChangeEvent"/> mapped to the contract DTO),
/// delivery is ordered per workspace (one dispatch queue per bus instance) and pattern-filtered
/// per subscription, and handler failures or timeouts are reported and contained.
/// <see cref="Notify"/> never throws. The payload carries the event's workspace id (the harness
/// stamps the run's workspace), falling back to this bus's own id.
/// </summary>
public sealed class FileChangeBus : IFileChangeSink, IFileChangeBus, IDisposable
{
    private readonly string _workspaceId;
    private readonly IExtensionLog _log;
    private readonly HookRunnerOptions _options;
    private readonly object _gate = new();
    private readonly Channel<FileChangedPayload> _events =
        Channel.CreateUnbounded<FileChangedPayload>();
    private readonly List<Subscription> _subscriptions = [];
    private readonly List<Drain> _drains = [];
    private long _sequence;
    private long _enqueued;
    private long _processed;
    private Task? _pump;
    private bool _drainStarted;
    private bool _disposed;

    public FileChangeBus(
        string workspaceId,
        IExtensionLog? log = null,
        HookRunnerOptions? options = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        _workspaceId = workspaceId;
        _log = log ?? NullExtensionLog.Instance;
        _options = options ?? new HookRunnerOptions();
    }

    public string WorkspaceId => _workspaceId;

    /// <summary>
    /// Publishes a successful mutation; callers are never affected by handler failures. Once a
    /// drain has begun, further notifications are dropped with one log line each.
    /// </summary>
    public void Notify(FileChangeEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentException.ThrowIfNullOrWhiteSpace(change.AbsolutePath);

        bool accepted = false;
        bool closed;
        lock (_gate)
        {
            closed = _disposed;
            if (!closed && !_drainStarted)
            {
                if (_events.Writer.TryWrite(ToPayload(change)))
                {
                    _enqueued++;
                    _pump ??= Task.Run(PumpAsync);
                    accepted = true;
                }
                else
                {
                    closed = true;
                }
            }
        }

        if (accepted)
        {
            return;
        }

        _log.Warn(
            closed
                ? $"file change: notification for '{change.AbsolutePath}' dropped: the bus is closed."
                : $"file change: notification for '{change.AbsolutePath}' dropped: a drain has begun."
        );
    }

    public IDisposable Subscribe(IFileChangedHandler handler, string? pathPattern = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new Subscription(handler, pathPattern, _sequence++);
        lock (_gate)
        {
            _subscriptions.Add(subscription);
        }

        return new Unsubscriber(this, subscription);
    }

    /// <summary>
    /// Completes when every notification enqueued so far has been dispatched, without beginning the
    /// drain: notifications arriving afterwards are still delivered, so a flush may be repeated as
    /// later notifications arrive. Unlike <see cref="DrainAsync"/>, the one-way session-end latch,
    /// it never ends delivery and never drops anything.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        Task completion;
        lock (_gate)
        {
            if (_processed >= _enqueued)
            {
                return;
            }

            var source = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _drains.Add(new Drain(_enqueued, source));
            completion = source.Task;
        }

        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Completes when every notification enqueued so far has been dispatched. Unlike
    /// <see cref="FlushAsync"/>, it begins the one-way drain: notifications arriving afterwards are
    /// dropped and logged, so a slow handler can never extend session teardown. Use it at session
    /// end; use the flush when delivery must continue after it completes.
    /// </summary>
    public async ValueTask DrainAsync(CancellationToken cancellationToken = default)
    {
        Task completion;
        lock (_gate)
        {
            _drainStarted = true;
            if (_processed >= _enqueued)
            {
                return;
            }

            var source = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _drains.Add(new Drain(_enqueued, source));
            completion = source.Task;
        }

        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _drainStarted = true;
        }

        _events.Writer.TryComplete();
    }

    private FileChangedPayload ToPayload(FileChangeEvent change) =>
        new(
            change.AbsolutePath,
            change.RelativePath,
            string.IsNullOrWhiteSpace(change.WorkspaceId) ? _workspaceId : change.WorkspaceId,
            change.RunId,
            WireKind(change.Kind),
            change.Source
        );

    private static string WireKind(FileChangeKind kind) =>
        kind switch
        {
            FileChangeKind.Created => "created",
            _ => "modified",
        };

    private async Task PumpAsync()
    {
        await foreach (FileChangedPayload payload in _events.Reader.ReadAllAsync())
        {
            await DispatchAsync(payload).ConfigureAwait(false);
            CompleteOne();
        }
    }

    private async ValueTask DispatchAsync(FileChangedPayload payload)
    {
        Subscription[] matching;
        lock (_gate)
        {
            matching =
            [
                .. _subscriptions
                    .Where(subscription =>
                        PatternFilter.Matches(subscription.PathPattern, payload.Path)
                    )
                    .OrderByDescending(subscription => subscription.Handler.Priority)
                    .ThenBy(subscription => subscription.Sequence),
            ];
        }

        foreach (Subscription subscription in matching)
        {
            await InvokeAsync(subscription.Handler, payload).ConfigureAwait(false);
        }
    }

    private async ValueTask InvokeAsync(IFileChangedHandler handler, FileChangedPayload payload)
    {
        using var timeout = new CancellationTokenSource();
        Task task;
        try
        {
            task = handler.OnFileChangedAsync(payload, timeout.Token).AsTask();
        }
        catch (Exception exception)
        {
            LogFailure(handler, exception.Message);
            return;
        }

        Task winner = await Task.WhenAny(task, Task.Delay(_options.HandlerTimeout, timeout.Token));
        if (winner != task)
        {
            timeout.Cancel();
            Observe(task);
            _log.Error(
                $"file change: handler {handler.GetType().Name} timed out after {_options.HandlerTimeout.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} ms; its failure is reported and delivery continues."
            );
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogFailure(handler, $"{exception.GetType().FullName}: {exception.Message}");
        }
    }

    private void LogFailure(IFileChangedHandler handler, string reason) =>
        _log.Error(
            $"file change: handler {handler.GetType().Name} failed: {reason}; its failure is reported and delivery continues."
        );

    private static void Observe(Task task) =>
        _ = task.ContinueWith(static completed => _ = completed.Exception, TaskScheduler.Default);

    private void CompleteOne()
    {
        lock (_gate)
        {
            _processed++;
            for (int index = _drains.Count - 1; index >= 0; index--)
            {
                if (_drains[index].Target <= _processed)
                {
                    _drains[index].Completion.TrySetResult();
                    _drains.RemoveAt(index);
                }
            }
        }
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_gate)
        {
            _subscriptions.Remove(subscription);
        }
    }

    private sealed record Subscription(
        IFileChangedHandler Handler,
        string? PathPattern,
        long Sequence
    );

    private sealed record Drain(long Target, TaskCompletionSource Completion);

    private sealed class Unsubscriber(FileChangeBus bus, Subscription subscription) : IDisposable
    {
        public void Dispose() => bus.Unsubscribe(subscription);
    }
}

using System.Threading.Channels;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The in-process file change bus: the core emits one <see cref="FileChangedPayload"/> per
/// successful queued mutation, delivery is ordered per workspace (one dispatch queue per bus
/// instance) and pattern-filtered per subscription, and handler failures or timeouts are reported
/// and contained. <see cref="Notify"/> never throws. A mutation outside the workspace root still
/// carries this bus's workspace id.
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

    /// <summary>Publishes a successful mutation; callers are never affected by handler failures.</summary>
    public void Notify(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _enqueued++;
            _pump ??= Task.Run(PumpAsync);
        }

        _events.Writer.TryWrite(new FileChangedPayload(absolutePath, _workspaceId));
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

    /// <summary>Completes when every notification enqueued so far has been dispatched.</summary>
    public async ValueTask DrainAsync(CancellationToken cancellationToken = default)
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

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _events.Writer.TryComplete();
    }

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

using System.Threading.Channels;

namespace FakeLsp;

/// <summary>
/// A minimal in-memory LSP-like server: requests and responses travel over channels, so a test can
/// prove a background service serves requests while it is running. Start is idempotent, stop is
/// idempotent, and the server can be started again after a stop.
/// </summary>
public sealed class FakeLspServer
{
    private readonly Channel<string> _requests = Channel.CreateUnbounded<string>();
    private readonly Channel<string> _responses = Channel.CreateUnbounded<string>();
    private readonly object _gate = new();
    private CancellationTokenSource? _loop;
    private Task? _running;
    private int _starts;
    private int _stops;

    public int StartCount => _starts;

    public int StopCount => _stops;

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_running is not null)
            {
                return ValueTask.CompletedTask;
            }

            CancellationTokenSource loop = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            _loop = loop;
            _running = Task.Run(() => ServeAsync(loop.Token));
        }

        Interlocked.Increment(ref _starts);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        Task? running;
        CancellationTokenSource? loop;
        lock (_gate)
        {
            running = _running;
            loop = _loop;
            _running = null;
            _loop = null;
        }

        if (running is null)
        {
            return;
        }

        loop!.Cancel();
        try
        {
            await running.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        loop.Dispose();
        Interlocked.Increment(ref _stops);
    }

    /// <summary>Sends a request and waits for the server's answer.</summary>
    public async ValueTask<string> RequestAsync(string request, CancellationToken cancellationToken)
    {
        await _requests.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        return await _responses.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (string request in _requests.Reader.ReadAllAsync(cancellationToken))
            {
                await _responses
                    .Writer.WriteAsync($"pong:{request}", cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }
}

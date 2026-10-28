namespace Lunate.Protocols;

/// <summary>
/// The startup watchdog for one MCP server start. The bound is an inactivity bound, not a total
/// startup budget: the child's output (its stderr lines) is the only liveness signal the client can
/// observe while it waits on the protocol stream, so <see cref="Touch"/> restarts the bound and a
/// server that is merely slow may take longer than the bound in total. A server that produces no
/// output at all for the bound is hung, and <see cref="Token"/> is cancelled with
/// <see cref="Stalled"/> set so the caller can report a stall instead of a failed start.
/// </summary>
internal sealed class StartupLiveness : IDisposable
{
    private readonly TimeSpan stallBound;
    private readonly CancellationTokenSource stallSource = new();
    private readonly CancellationTokenSource linkedSource;
    private readonly Timer timer;
    private volatile bool stalled;
    private volatile bool completed;
    private int disposed;

    /// <param name="stallBound">How long the child may stay silent before it counts as hung.</param>
    /// <param name="outer">The caller's token; cancelling it cancels <see cref="Token"/> too.</param>
    public StartupLiveness(TimeSpan stallBound, CancellationToken outer)
    {
        this.stallBound = stallBound;
        linkedSource = CancellationTokenSource.CreateLinkedTokenSource(outer, stallSource.Token);
        timer = new Timer(_ => OnStall(), state: null, stallBound, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Linked from the caller's token and the stall; pass it to the start call.</summary>
    public CancellationToken Token => linkedSource.Token;

    /// <summary>
    /// Whether the timer fired, meaning the child was silent for the whole bound. A completion that
    /// beat a late timer wins: a successful start is never reported as a stall.
    /// </summary>
    public bool Stalled => !completed && stalled;

    /// <summary>Restarts the silent window. Safe from any thread; a no-op after completion.</summary>
    public void Touch()
    {
        if (completed || Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        try
        {
            timer.Change(stallBound, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Disposed between the check and the call: nothing left to watch.
        }
    }

    /// <summary>
    /// Stops watching after a successful start, so a timer that fires late cannot cancel anything.
    /// </summary>
    public void Complete()
    {
        completed = true;
        try
        {
            timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Disposing the timer first keeps a firing callback from racing the token sources.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        timer.Dispose();
        stallSource.Dispose();
        linkedSource.Dispose();
    }

    private void OnStall()
    {
        if (completed || Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        stalled = true;
        try
        {
            stallSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A timer callback that loses the race with Dispose must not throw on the pool.
        }
    }
}

using System.Diagnostics;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// Loads the thread pool on purpose, the way a shared CI runner is. Every worker is taken by a work
/// item blocked on a gate; <see cref="Load"/> then keeps more work queued than the pool has workers,
/// so a continuation — a handler's included — waits for a thread instead of running and the pool
/// reports itself behind. Work is queued from a thread of its own, so it lands on the pool's global
/// queue rather than in the local queue of the worker running the test: a local queue is only stolen
/// from when a worker is idle, which is the opposite of loading the pool. The blockers wait on a gate
/// rather than sleeping, which is the kind of block the pool notices, so it can inject a thread.
/// </summary>
internal sealed class PoolStarvation : IDisposable
{
    private const int PollMilliseconds = 5;

    private readonly ManualResetEventSlim _gate = new(false);

    private volatile bool _released;
    private int _blocking;
    private int _loaded;
    private Thread? _keeper;

    public PoolStarvation()
    {
        int workers = Math.Max(Environment.ProcessorCount, ThreadPool.ThreadCount);
        Queue(workers);

        // Hand back only once every worker is blocked, so the caller's dispatch is the next thing
        // waiting for a thread rather than racing the harness.
        long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        while (
            Volatile.Read(ref _blocking) < ThreadPool.ThreadCount
            && Stopwatch.GetTimestamp() < deadline
        )
        {
            Thread.Sleep(PollMilliseconds);
        }
    }

    /// <summary>
    /// Keeps more work queued than there are workers from now on. Called once the dispatch has queued
    /// its first continuation, so what waits behind the pile is the runner's verdict and not the
    /// handler.
    /// </summary>
    public void Load()
    {
        if (Interlocked.Exchange(ref _loaded, 1) != 0)
        {
            return;
        }

        _keeper = new Thread(KeepLoaded) { IsBackground = true };
        _keeper.Start();
    }

    public void Dispose()
    {
        _released = true;
        _keeper?.Join(TimeSpan.FromSeconds(10));
        _gate.Set();
        for (var waited = 0; waited < 5_000 && Volatile.Read(ref _blocking) > 0; waited += 10)
        {
            Thread.Sleep(10);
        }

        // Only when every blocker has left: one still waiting on it would fault on a disposed gate.
        if (Volatile.Read(ref _blocking) == 0)
        {
            _gate.Dispose();
        }
    }

    private void Queue(int count)
    {
        var queueing = new Thread(() =>
        {
            for (var index = 0; index < count; index++)
            {
                ThreadPool.QueueUserWorkItem(
                    static state => ((PoolStarvation)state!).Block(),
                    this
                );
            }
        });
        queueing.Start();
        queueing.Join();
    }

    private void KeepLoaded()
    {
        while (!_released)
        {
            // A margin over the worker count, because the pool keeps injecting threads as it works
            // through the pile and the runner's witness is "more waiting than workers".
            int target = (ThreadPool.ThreadCount * 2) + 1;
            if (ThreadPool.PendingWorkItemCount <= target)
            {
                Queue(target);
            }

            Thread.Sleep(10);
        }
    }

    private void Block()
    {
        Interlocked.Increment(ref _blocking);
        try
        {
            _gate.Wait(TimeSpan.FromSeconds(60));
        }
        finally
        {
            Interlocked.Decrement(ref _blocking);
        }
    }
}

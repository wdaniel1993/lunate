using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// Proves the sampled paint chain's health directly instead of inferring it from a wait timeout.
/// The live area's frame subscription is <c>Observable.Interval(FrameInterval, scheduler)</c>: every
/// tick re-arms the next one from *inside* the tick's own action. If a re-arm is ever lost the
/// chain is dead for the rest of the test and no frame is painted again, which is the shape of
/// "writes=2, the last frame is blank". These probes assert the invariant "after every executed
/// tick, another one is queued" rather than watching for a symptom.
/// </summary>
public sealed class ManualSchedulerChainProbe
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(33);

    private static IDisposable Post(IScheduler scheduler, TimeSpan due, Action action) =>
        scheduler.Schedule(
            action,
            due,
            static (_, work) =>
            {
                work();
                return Disposable.Empty;
            }
        );

    [Fact]
    public void Every_sampled_tick_rearms_the_next_one()
    {
        var scheduler = new ManualScheduler();
        long ticks = 0;
        using IDisposable subscription = Observable
            .Interval(Frame, scheduler)
            .Subscribe(_ => Interlocked.Increment(ref ticks));

        // The subscription arms the first tick when it is created; from then on the chain must
        // always hold exactly one pending tick.
        Assert.True(scheduler.PendingWorkCount >= 1, "the first tick was never armed");

        for (var i = 0; i < 2000; i++)
        {
            scheduler.Advance(Frame);
            Assert.Equal(i + 1, Interlocked.Read(ref ticks));
            Assert.True(
                scheduler.PendingWorkCount >= 1,
                $"the chain died after tick {i + 1}: no re-armed tick is queued"
            );
        }
    }

    /// <summary>
    /// The real shape: the session thread posts stimuli (and its own scheduling) while the test
    /// thread advances. A re-arm can be queued by either thread, so the queue is read and written
    /// concurrently.
    /// </summary>
    [Fact]
    public async Task The_chain_survives_concurrent_posting_from_the_session_thread()
    {
        var scheduler = new ManualScheduler();
        long ticks = 0;
        using IDisposable subscription = Observable
            .Interval(Frame, scheduler)
            .Subscribe(_ => Interlocked.Increment(ref ticks));

        using var stop = new CancellationTokenSource();
        CancellationToken ct = TestContext.Current.CancellationToken;
        Task poster = Task.Run(
            () =>
            {
                var noise = 0;
                while (!stop.IsCancellationRequested)
                {
                    // The two shapes the live area actually produces: a delayed re-arm and a
                    // zero-delay post.
                    Post(scheduler, TimeSpan.Zero, () => noise++);
                    Post(scheduler, Frame, () => noise++);
                }
            },
            ct
        );

        try
        {
            for (var i = 0; i < 2000; i++)
            {
                scheduler.Advance(Frame);
                Assert.True(
                    Interlocked.Read(ref ticks) >= i + 1,
                    $"the chain stalled at tick {Interlocked.Read(ref ticks)} of {i + 1}"
                );
                Assert.True(
                    scheduler.PendingWorkCount >= 1,
                    $"the chain died after tick {i + 1}: no re-armed tick is queued"
                );
            }
        }
        finally
        {
            await stop.CancelAsync();
            await poster;
        }
    }

    /// <summary>
    /// The paint path itself: a live area over the scripted console must keep painting while the
    /// test advances, no matter what the session thread posts meanwhile.
    /// </summary>
    [Fact]
    public async Task A_live_area_keeps_painting_while_stimuli_arrive()
    {
        var console = new ScriptedConsoleIO { Size = new ConsoleSize(80, 24) };
        var scheduler = new ManualScheduler();
        using var area = new LiveArea(
            console,
            scheduler,
            readKeys: false,
            scrollback: new Spectre.Console.Testing.TestConsole()
        );
        area.Start();

        using var stop = new CancellationTokenSource();
        CancellationToken ct = TestContext.Current.CancellationToken;
        var firstStimulus = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task poster = Task.Run(
            async () =>
            {
                var n = 0;
                while (!stop.IsCancellationRequested)
                {
                    area.SetInput(new InputLineState($"line {n}", 0));
                    area.SetNotice($"notice {n}");
                    if (n++ == 0)
                    {
                        firstStimulus.TrySetResult();
                    }

                    await Task.Delay(1, CancellationToken.None);
                }
            },
            ct
        );

        try
        {
            // 300 advances take microseconds, so without this the loop can finish before the
            // poster's first write exists: the live area then has nothing to repaint and the
            // assertion fails on scheduling luck (Ubuntu CI lost that race, macOS won it).
            await firstStimulus.Task;

            int before = console.Writes.Count;
            for (var i = 0; i < 300; i++)
            {
                scheduler.Advance(Frame);
                await Task.Yield();
            }

            Assert.True(
                console.Writes.Count > before,
                $"no frame was painted in 300 advances (writes={console.Writes.Count})"
            );
        }
        finally
        {
            await stop.CancelAsync();
            await poster;
        }
    }
}

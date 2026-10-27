using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

using static Lunate.Extensibility.Tests.HookSemanticsSupport;

/// <summary>
/// The slow-hook verdict on a starved thread pool. A dispatch's wall time is the machine's to
/// inflate — a queued continuation waits for a worker — so a hook whose own work is nothing must not
/// be reported, while the dispatch is still counted. The pool is occupied deliberately and the
/// dispatch runs on its own thread, so what is starved is the pool under test, not the harness.
/// </summary>
public sealed class HookRunnerStarvationTests
{
    [Fact]
    public void A_hook_the_starved_pool_delayed_is_not_reported_slow()
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(Options(), log);
        var events = new List<SlowHookEvent>();
        runner.SlowHook += events.Add;
        runner.Register(
            "ext-fast",
            new TestToolCallingHandler(
                async (_, _) =>
                {
                    // No work at all: the only time this handler can cost is the pool's own.
                    await Task.Yield();
                    return new ToolCallingResult.Proceed(null);
                }
            )
        );

        using (var starvation = new PoolStarvation())
        {
            Dispatch(runner, starvation);
        }

        HookTimings timings = Assert.Single(runner.Snapshot("ext-fast"));
        Assert.Equal(1, timings.Calls);
        Assert.Equal(1, timings.Completed);
        Assert.Equal(0, timings.SlowCount);
        Assert.Equal(0, timings.MaxMs);
        Assert.True(
            timings.TotalMs >= 20,
            $"the harness did not delay the hook (total was {timings.TotalMs} ms)"
        );
        Assert.Empty(events);
        Assert.DoesNotContain(log.Messages, SlowWarning);
    }

    /// <summary>
    /// The other half of the rule: a crossing the machine took must not spend the hook's one warning,
    /// so the first dispatch the handler really is slow for still reports.
    /// </summary>
    [Fact]
    public void A_dismissed_crossing_does_not_spend_the_warning()
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(Options(), log);
        var events = new List<SlowHookEvent>();
        runner.SlowHook += events.Add;
        var work = 0;
        runner.Register(
            "ext-switch",
            new TestToolCallingHandler(
                async (_, _) =>
                {
                    if (Volatile.Read(ref work) is var milliseconds and > 0)
                    {
                        Thread.Sleep(milliseconds);
                    }
                    else
                    {
                        await Task.Yield();
                    }

                    return new ToolCallingResult.Proceed(null);
                }
            )
        );

        using (var starvation = new PoolStarvation())
        {
            Dispatch(runner, starvation);
        }

        Assert.Empty(events);

        // The same hook, the same handler, on a pool that has caught up: the crossing is the
        // handler's own now, and the warning it never spent still fires.
        Volatile.Write(ref work, 200);
        Dispatch(runner);

        HookTimings timings = Assert.Single(runner.Snapshot("ext-switch"));
        Assert.Equal(2, timings.Calls);
        Assert.Equal(1, timings.SlowCount);
        Assert.True(timings.MaxMs >= 200, $"max was {timings.MaxMs}");
        Assert.Single(events);
        Assert.Single(log.Messages, SlowWarning);
    }

    private static bool SlowWarning(string message) =>
        message.Contains("slow-hook threshold", StringComparison.Ordinal);

    private static HookRunnerOptions Options() =>
        new()
        {
            SlowHookThreshold = TimeSpan.FromMilliseconds(20),
            // A loaded pool delays a continuation for seconds; the hook must still be timed rather
            // than cut off, which is the case this file is about.
            HandlerTimeout = TimeSpan.FromSeconds(60),
        };

    private static void Dispatch(HookRunner runner, PoolStarvation? starvation = null)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                runner
                    .RunToolCallingAsync(Payload("loaded"), CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.Start();
        if (starvation is not null)
        {
            // The dispatch has queued its first continuation by now: from here the pile sits behind
            // it, so the runner's verdict waits for a thread while the handler did not have to.
            Thread.Sleep(20);
            starvation.Load();
        }
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the dispatch never finished");
        Assert.Null(failure);
    }
}

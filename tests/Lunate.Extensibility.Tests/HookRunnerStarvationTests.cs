using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

using static Lunate.Extensibility.Tests.HookSemanticsSupport;

/// <summary>
/// The slow-hook verdict when the machine, rather than the handler, is what a dispatch waited for.
/// The runner asks one question — is the pool behind? — and a test cannot hold a real pool still
/// while a handler runs, so the answer is injected. What is pinned here is the attribution: a
/// crossing the machine took is counted but not reported, and it does not spend the hook's one
/// warning, so the first crossing the handler really owns still reports.
/// </summary>
public sealed class HookRunnerStarvationTests
{
    [Fact]
    public void A_hook_the_starved_pool_delayed_is_not_reported_slow()
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(Options(machineIsBehind: () => true), log);
        var events = new List<SlowHookEvent>();
        runner.SlowHook += events.Add;
        runner.Register("ext-slow", new TestToolCallingHandler(Slow(80)));

        Dispatch(runner);

        HookTimings timings = Assert.Single(runner.Snapshot("ext-slow"));
        Assert.Equal(1, timings.Calls);
        Assert.Equal(1, timings.Completed);
        Assert.Equal(0, timings.SlowCount);
        // MaxMs stays 0 on this path by design — the crossing is not the hook's, so it is not
        // counted as one; TotalMs is what proves the dispatch really ran past the threshold.
        Assert.Equal(0, timings.MaxMs);
        Assert.True(
            timings.TotalMs >= 20,
            $"the dispatch was under the threshold ({timings.TotalMs} ms), so the rule was not exercised"
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
        bool machineIsBehind = true;
        var runner = new HookRunner(Options(machineIsBehind: () => machineIsBehind), log);
        var events = new List<SlowHookEvent>();
        runner.SlowHook += events.Add;
        var work = 80;
        runner.Register(
            "ext-switch",
            new TestToolCallingHandler(
                async (_, _) =>
                {
                    Thread.Sleep(Volatile.Read(ref work));
                    await Task.Yield();
                    return new ToolCallingResult.Proceed(null);
                }
            )
        );

        Dispatch(runner);
        Assert.Empty(events);

        // The same hook, the same handler, on a machine that has caught up: the crossing is the
        // handler's own now, and the warning the queue never spent still fires.
        machineIsBehind = false;
        Volatile.Write(ref work, 200);
        Dispatch(runner);

        HookTimings timings = Assert.Single(runner.Snapshot("ext-switch"));
        Assert.Equal(2, timings.Calls);
        Assert.Equal(1, timings.SlowCount);
        Assert.True(timings.MaxMs >= 200, $"max was {timings.MaxMs}");
        SlowHookEvent raised = Assert.Single(events);
        Assert.Equal("ext-switch", raised.ExtensionId);
        Assert.Single(log.Messages, SlowWarning);
    }

    private static Func<ToolCallingPayload, CancellationToken, ValueTask<ToolCallingResult>> Slow(
        int milliseconds
    ) =>
        async (_, _) =>
        {
            Thread.Sleep(milliseconds);
            await Task.Yield();
            return new ToolCallingResult.Proceed(null);
        };

    private static bool SlowWarning(string message) =>
        message.Contains("slow-hook threshold", StringComparison.Ordinal);

    private static HookRunnerOptions Options(Func<bool> machineIsBehind) =>
        new()
        {
            SlowHookThreshold = TimeSpan.FromMilliseconds(20),
            // A loaded pool delays a continuation for seconds; the hook must still be timed rather
            // than cut off, which is the case this file is about.
            HandlerTimeout = TimeSpan.FromSeconds(60),
            MachineBehind = machineIsBehind,
        };

    private static void Dispatch(HookRunner runner)
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the dispatch never finished");
        Assert.Null(failure);
    }
}

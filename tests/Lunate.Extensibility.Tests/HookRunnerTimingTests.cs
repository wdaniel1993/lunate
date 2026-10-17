using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

using static Lunate.Extensibility.Tests.HookSemanticsSupport;

/// <summary>
/// The hook-timing funnel: per (extension, hook) aggregation, warn-once with a growing slow count,
/// cancelled dispatches excluded from the maximum and the slow path, timeouts counted, child runs
/// landing in the session aggregate, the threshold option, and preparation exclusion.
/// </summary>
public sealed class HookRunnerTimingTests
{
    [Fact]
    public async Task Timings_aggregate_per_extension_and_hook()
    {
        var runner = new HookRunner(Options());
        runner.Register(
            "ext-a",
            new TestToolCallingHandler(
                (payload, _) =>
                    payload.Arguments.GetProperty("path").GetString() == "one" ? Slow(60) : Fast()
            )
        );
        runner.Register("ext-a", new TestRunSettledHandler((_, _) => ValueTask.CompletedTask));
        runner.Register("ext-b", new TestToolCallingHandler((_, _) => Fast()));

        await runner.RunToolCallingAsync(Payload("one"), Ct);
        await runner.RunToolCallingAsync(Payload("two"), Ct);
        await runner.RunRunSettledAsync(TestHookPayloads.RunSettled, Ct);

        IReadOnlyList<HookTimings> first = runner.Snapshot("ext-a");
        Assert.Equal(2, first.Count);
        Assert.Equal("RunSettled", first[0].Hook);
        Assert.Equal(1, first[0].Calls);
        Assert.Equal(0, first[0].SlowCount);
        Assert.Equal("ToolCalling", first[1].Hook);
        Assert.Equal(2, first[1].Calls);
        Assert.Equal(1, first[1].SlowCount);
        Assert.True(first[1].MaxMs >= 45, $"max was {first[1].MaxMs}");
        Assert.True(first[1].TotalMs >= first[1].MaxMs);

        HookTimings second = Assert.Single(runner.Snapshot("ext-b"));
        Assert.Equal("ToolCalling", second.Hook);
        Assert.Equal(2, second.Calls);
        Assert.Equal(0, second.SlowCount);
        Assert.True(second.MaxMs < 20, $"max was {second.MaxMs}");
        Assert.Empty(runner.Snapshot("ext-absent"));
    }

    [Fact]
    public async Task A_slow_hook_warns_once_and_later_crossings_only_count()
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(Options(), log);
        var events = new List<SlowHookEvent>();
        runner.SlowHook += events.Add;
        runner.Register("ext-slow", new TestToolCallingHandler((_, _) => Slow(50)));

        for (var call = 0; call < 3; call++)
        {
            await runner.RunToolCallingAsync(Payload("path"), Ct);
        }

        string warning = Assert.Single(
            log.Messages,
            message => message.StartsWith("warn: hook ToolCalling", StringComparison.Ordinal)
        );
        Assert.Contains("ext-slow", warning, StringComparison.Ordinal);
        Assert.Contains("slow-hook threshold of 10 ms", warning, StringComparison.Ordinal);
        Assert.Contains("/extensions", warning, StringComparison.Ordinal);

        HookTimings timings = Assert.Single(runner.Snapshot("ext-slow"));
        Assert.Equal(3, timings.Calls);
        Assert.Equal(3, timings.SlowCount);
        Assert.True(timings.MaxMs >= 40, $"max was {timings.MaxMs}");

        SlowHookEvent raised = Assert.Single(events);
        Assert.Equal("ext-slow", raised.ExtensionId);
        Assert.Equal("ToolCalling", raised.Hook);
        Assert.Equal(TimeSpan.FromMilliseconds(10), raised.Threshold);
        Assert.True(raised.Elapsed.TotalMilliseconds >= 40);
    }

    [Fact]
    public async Task A_cancelled_dispatch_is_counted_but_never_slow()
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(Options(), log);
        var events = new List<SlowHookEvent>();
        runner.SlowHook += events.Add;
        runner.Register(
            "ext-cancel",
            new TestToolCallingHandler(
                async (_, ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return new ToolCallingResult.Proceed(null);
                }
            )
        );
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunToolCallingAsync(Payload("path"), cancel.Token).AsTask()
        );

        HookTimings timings = Assert.Single(runner.Snapshot("ext-cancel"));
        Assert.Equal(1, timings.Calls);
        Assert.Equal(1, timings.Cancelled);
        Assert.Equal(0, timings.TimedOut);
        Assert.Equal(0, timings.MaxMs);
        Assert.Equal(0, timings.SlowCount);
        Assert.True(timings.TotalMs >= 40, $"total was {timings.TotalMs}");
        Assert.Empty(events);
        Assert.DoesNotContain(
            log.Messages,
            message => message.Contains("slow", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_timed_out_dispatch_counts_as_the_hooks_cost()
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(
            Options() with
            {
                HandlerTimeout = TimeSpan.FromMilliseconds(60),
            },
            log
        );
        var events = new List<SlowHookEvent>();
        runner.SlowHook += events.Add;
        runner.Register(
            "ext-hang",
            new TestToolCallingHandler(
                async (_, ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return new ToolCallingResult.Proceed(null);
                }
            )
        );

        var result = Assert.IsType<ToolCallingResult.Block>(
            await runner.RunToolCallingAsync(Payload("path"), Ct)
        );
        Assert.Contains("ext-hang", result.Reason, StringComparison.Ordinal);

        HookTimings timings = Assert.Single(runner.Snapshot("ext-hang"));
        Assert.Equal(1, timings.Calls);
        Assert.Equal(1, timings.TimedOut);
        Assert.Equal(0, timings.Cancelled);
        Assert.Equal(1, timings.SlowCount);
        Assert.True(timings.MaxMs >= 50, $"max was {timings.MaxMs}");
        Assert.Single(events);

        Assert.Single(
            log.Messages,
            message => message.StartsWith("warn: hook ToolCalling", StringComparison.Ordinal)
        );
        Assert.Contains(
            log.Messages,
            message => message.StartsWith("error: hook ToolCalling", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Child_runs_land_in_the_session_aggregate()
    {
        var runner = new HookRunner(Options());
        runner.Register("ext-parent", new TestToolCallingHandler((_, _) => Fast()));

        await runner.RunToolCallingAsync(Payload("path"), Ct);
        await runner.RunToolCallingAsync(Payload("path") with { RunId = "r1/1" }, Ct);

        HookTimings timings = Assert.Single(runner.Snapshot("ext-parent"));
        Assert.Equal(2, timings.Calls);
    }

    [Fact]
    public async Task The_slow_hook_threshold_option_is_honored()
    {
        var strictLog = new RecordingExtensionLog();
        var strict = new HookRunner(Options(), strictLog);
        strict.Register("ext-threshold", new TestToolCallingHandler((_, _) => Slow(30)));

        var laxLog = new RecordingExtensionLog();
        var lax = new HookRunner(
            Options() with
            {
                SlowHookThreshold = TimeSpan.FromMilliseconds(200),
            },
            laxLog
        );
        lax.Register("ext-threshold", new TestToolCallingHandler((_, _) => Slow(30)));

        await strict.RunToolCallingAsync(Payload("path"), Ct);
        await lax.RunToolCallingAsync(Payload("path"), Ct);

        Assert.Equal(1, Assert.Single(strict.Snapshot("ext-threshold")).SlowCount);
        Assert.Contains(
            strictLog.Messages,
            message => message.StartsWith("warn: hook ToolCalling", StringComparison.Ordinal)
        );
        HookTimings laxTimings = Assert.Single(lax.Snapshot("ext-threshold"));
        Assert.Equal(0, laxTimings.SlowCount);
        Assert.True(laxTimings.MaxMs >= 20, $"max was {laxTimings.MaxMs}");
        Assert.DoesNotContain(
            laxLog.Messages,
            message => message.Contains("slow-hook threshold", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Reported_preparation_is_excluded_from_the_dispatch()
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(Options(), log);
        var handler = new PreparableToolCallingHandler(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.Zero
        );
        runner.Register("ext-remote", handler);

        await runner.RunToolCallingAsync(Payload("cold"), Ct);
        await runner.RunToolCallingAsync(Payload("warm"), Ct);

        HookTimings timings = Assert.Single(runner.Snapshot("ext-remote"));
        Assert.Equal(2, timings.Calls);
        Assert.Equal(0, timings.SlowCount);
        Assert.True(timings.MaxMs < 50, $"max was {timings.MaxMs}");
        Assert.Equal(2, handler.ConsumeCalls);
        Assert.DoesNotContain(
            log.Messages,
            message => message.Contains("slow-hook threshold", StringComparison.Ordinal)
        );
    }

    private static HookRunnerOptions Options() =>
        new() { SlowHookThreshold = TimeSpan.FromMilliseconds(10) };

    private static ValueTask<ToolCallingResult> Fast() =>
        ValueTask.FromResult<ToolCallingResult>(new ToolCallingResult.Proceed(null));

    private static async ValueTask<ToolCallingResult> Slow(int milliseconds)
    {
        await Task.Delay(milliseconds, Ct).ConfigureAwait(false);
        return new ToolCallingResult.Proceed(null);
    }

    /// <summary>A handler that reports a queued preparation time once per dispatch.</summary>
    private sealed class PreparableToolCallingHandler(params TimeSpan[] preparations)
        : IToolCallingHandler,
            IPreparationTimeReporter
    {
        private readonly Queue<TimeSpan> _preparations = new(preparations);

        public int Priority => 0;

        public int ConsumeCalls { get; private set; }

        public ValueTask<ToolCallingResult> HandleAsync(
            ToolCallingPayload payload,
            CancellationToken cancellationToken
        ) => Fast();

        public TimeSpan ConsumePreparationTime()
        {
            ConsumeCalls++;
            return _preparations.Count > 0 ? _preparations.Dequeue() : TimeSpan.Zero;
        }
    }
}

using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The slow-hook notice as it reaches the slot: a notice posted while a submission's or a queued
/// follow-up's input pipeline runs is cleared by a turn start (<c>RunStarted</c> clears the notice
/// area). No frame is pumped between the two unless the clock happens to tick, so the clear arrives
/// on a notice no paint has shown. The runner raises the notice only for the first crossing of the
/// threshold, so a notice lost there is lost for the rest of the process — it must be shown once.
/// </summary>
public sealed class SlowHookNoticeClearTests
{
    private const string Notice = "above the slow threshold";

    /// <summary>
    /// The shipped flake's own path with the clock held still: the queued follow-up's slow hook
    /// posts the notice, the turn ends, and the next turn's <c>RunStarted</c> clears it before any
    /// frame has painted it.
    /// </summary>
    [Fact]
    public async Task A_queued_follow_up_notice_survives_the_turn_that_starts_next()
    {
        var runner = new HookRunner(
            new HookRunnerOptions { SlowHookThreshold = TimeSpan.FromMilliseconds(20) }
        );
        runner.Register("ext-slow", new SlowInputHandler(TimeSpan.FromMilliseconds(80), "steer"));
        using var host = new InteractiveSessionHost(hooks: runner);
        var log = new NoticeSlotLog();
        host.Session.ObserveNotices(log.Record);
        host.Console.Size = new ConsoleSize(600, 24);
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("queued"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("steer");
        host.Console.SendEnter();
        // The follow-up's hook posts the notice; the clock stays still, so nothing paints it yet.
        await host.WaitUntilAsync(() => log.Changes(NoticeSlotChange.Posted).Count == 1);

        // The turn unwinds, the queue drains and the next turn opens: its RunStarted clears the
        // notice area, still with no frame in between. This wait spans the turn boundary — the
        // run writes its session and history before the queue drains — so it takes the
        // loaded-runner ceiling; a 10 s window is what timed out on the Windows leg of #225 and
        // again on the macOS leg of #231 while the session was doing exactly this.
        host.Client.Release(1);
        await host.WaitUntilAsync(
            () => log.Changes(NoticeSlotChange.Cleared).Count == 1,
            WaitBudgets.CrossTurn
        );

        NoticeSlotEntry clear = log.Changes(NoticeSlotChange.Cleared)[0];
        Assert.True(clear.Deferred, $"the next turn took a notice no frame had shown:\n{log}");
        Assert.Empty(log.LostNotices);

        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return log.Changes(NoticeSlotChange.Painted).Count == 1;
        });

        // The queued follow-up's own turn, still on the far side of the boundary above.
        await host.Client.WaitForCallAsync(2, WaitBudgets.CrossTurn);
        host.Console.Complete();
        await run;
    }

    /// <summary>
    /// The other side of the same clear: a slow hook during the submission itself posts the notice
    /// before the turn starts, and that turn's <c>RunStarted</c> takes it in the same breath.
    /// </summary>
    [Fact]
    public async Task A_notice_posted_by_a_submission_survives_the_turn_it_starts()
    {
        var runner = new HookRunner(
            new HookRunnerOptions { SlowHookThreshold = TimeSpan.FromMilliseconds(20) }
        );
        runner.Register("ext-slow", new SlowInputHandler(TimeSpan.FromMilliseconds(80)));
        using var host = new InteractiveSessionHost(hooks: runner);
        var log = new NoticeSlotLog();
        host.Session.ObserveNotices(log.Record);
        host.Console.Size = new ConsoleSize(600, 24);
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await host.WaitUntilAsync(() => log.Changes(NoticeSlotChange.Cleared).Count == 1);

        NoticeSlotEntry clear = log.Changes(NoticeSlotChange.Cleared)[0];
        Assert.True(clear.Deferred, $"the turn start took a notice no frame had shown:\n{log}");
        Assert.Empty(log.LostNotices);

        // Shown once, then the held clear takes it away: announced once, not repeatedly.
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return log.Changes(NoticeSlotChange.Painted).Count == 1;
        });
        host.Advance(33);

        Assert.DoesNotContain(Notice, host.LastFrame, StringComparison.Ordinal);
        Assert.Single(log.Changes(NoticeSlotChange.Painted));

        host.Client.Release(1);
        host.Console.Complete();
        await run;
    }

    private sealed class SlowInputHandler(TimeSpan delay, string? onlyFor = null)
        : IInputReceivedHandler
    {
        public int Priority => 0;

        public async ValueTask<InputReceivedResult> HandleAsync(
            InputReceivedPayload payload,
            CancellationToken cancellationToken
        )
        {
            if (onlyFor is null || string.Equals(payload.Text, onlyFor, StringComparison.Ordinal))
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            return new InputReceivedResult.PassThrough();
        }
    }
}

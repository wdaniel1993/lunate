using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The interactive slow-hook notice: the runner's one slow-hook event per (extension, hook)
/// renders as one dim notice naming the extension, the hook, the elapsed time and the threshold
/// and pointing at <c>/extensions</c>.
/// </summary>
public sealed class SlowHookNoticeTests
{
    /// <summary>
    /// The runner these tests drive: a 20 ms threshold so the 80 ms hook crosses it, and the
    /// machine-behind verdict pinned to "not behind". A crossing the machine took is counted and
    /// deliberately not reported, and the gate's loaded legs read as behind — on run 40731375207
    /// the turn ran for 30 s and not one of its 688 frames carried the notice, because the pool
    /// dismissed the crossing before the slot heard of it. What is under test is the notice the
    /// handler's own crossing raises, so the verdict is the test's to set.
    /// </summary>
    private static HookRunnerOptions Options() =>
        new() { SlowHookThreshold = TimeSpan.FromMilliseconds(20), MachineBehind = () => false };

    [Fact]
    public async Task A_first_slow_dispatch_renders_one_notice()
    {
        var runner = new HookRunner(Options());
        runner.Register("ext-slow", new SlowInputHandler(TimeSpan.FromMilliseconds(80)));
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
        // The notice is an event on the journal, not a number in this file: the wait pumps a frame
        // per poll and ends on the paint the slot records. The frame literal it replaces was the
        // one that failed the de-AT pass of #255 at 30.49 s — the runner spent forty-odd seconds
        // starting this turn behind the rest of the solution's process-spawning suites, so the
        // clock expired while the session was doing exactly what the test asked.
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return log.Changes(NoticeSlotChange.Painted).Count == 1;
        });

        // The slot's own history, so a red says whether the notice was never raised or raised and
        // then taken: one post, one paint, and the paint carried the text.
        Assert.Single(log.Changes(NoticeSlotChange.Posted));
        NoticeSlotEntry painted = Assert.Single(log.Changes(NoticeSlotChange.Painted));
        Assert.Contains("above the slow threshold", painted.Text, StringComparison.Ordinal);
        Assert.Contains("hook InputReceived", host.Frames, StringComparison.Ordinal);
        Assert.Contains("ext-slow", host.Frames, StringComparison.Ordinal);
        Assert.Contains("/extensions", host.Frames, StringComparison.Ordinal);

        host.Client.Release(1);
        // The queued follow-up starts after the turn unwinds.
        await host.Client.WaitForCallAsync(2);
        host.Console.Complete();
        await run;
    }

    private sealed class SlowInputHandler(TimeSpan delay) : IInputReceivedHandler
    {
        public int Priority => 0;

        public async ValueTask<InputReceivedResult> HandleAsync(
            InputReceivedPayload payload,
            CancellationToken cancellationToken
        )
        {
            if (string.Equals(payload.Text, "steer", StringComparison.Ordinal))
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            return new InputReceivedResult.PassThrough();
        }
    }
}

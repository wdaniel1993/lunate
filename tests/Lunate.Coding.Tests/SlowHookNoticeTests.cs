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
    [Fact]
    public async Task A_first_slow_dispatch_renders_one_notice()
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

        NoticeSlotEntry painted = log.Changes(NoticeSlotChange.Painted)[0];
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

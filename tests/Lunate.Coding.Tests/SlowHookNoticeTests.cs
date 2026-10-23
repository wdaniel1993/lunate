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
        // The de-AT pass runs the whole solution's process-spawning suites in parallel; on a loaded
        // runner the 80 ms hook plus the frame pump can starve well past the in-process default, so
        // this wait uses the spawn-window idiom rather than the 10 s in-process one.
        await host.WaitUntilAsync(
            () =>
            {
                host.Advance(33);
                return host.Frames.Contains("above the slow threshold", StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(30)
        );

        Assert.Contains("hook InputReceived", host.Frames, StringComparison.Ordinal);
        Assert.Contains("ext-slow", host.Frames, StringComparison.Ordinal);
        Assert.Contains("/extensions", host.Frames, StringComparison.Ordinal);

        host.Client.Release(1);
        // TEMPORARY DIAGNOSTIC (CI-only hang): dump the state if the follow-up never starts.
        try
        {
            await host.WaitUntilAsync(
                () =>
                {
                    host.Advance(33);
                    return host.Client.Requests.Count >= 2;
                },
                TimeSpan.FromSeconds(20)
            );
        }
        catch (TimeoutException)
        {
            Assert.Fail(
                $"DIAG queued={host.Session.QueuedFollowUpCount} running={host.Session.IsRunning} "
                    + $"inputLen={host.Session.Input.Text.Length} requests={host.Client.Requests.Count} "
                    + $"bash={host.Session.Input.Text}"
            );
        }

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

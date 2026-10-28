using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// A follow-up whose input hooks finish after the running turn has already drained the queue. The
/// entry used to be enqueued into a session with nothing left to drain it, so the message silently
/// never ran. The enqueue path opens the turn itself when the session went idle meanwhile.
/// </summary>
public sealed class LateFollowUpTests
{
    [Fact]
    public async Task A_follow_up_that_finishes_after_the_turn_ends_still_runs()
    {
        var runner = new HookRunner(new HookRunnerOptions());
        runner.Register("ext-slow", new SlowSecondInputHandler(TimeSpan.FromMilliseconds(600)));
        using var host = new InteractiveSessionHost(hooks: runner);
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("second"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        host.Console.SendText("late");
        host.Console.SendEnter();

        // Release the first turn at once: the slow hook for "late" outlives it, so the drain runs
        // against an empty queue before the entry exists.
        host.Client.Release(1);

        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.Client.Requests.Count >= 2;
        });

        host.Console.Complete();
        await run;
    }

    private sealed class SlowSecondInputHandler(TimeSpan delay) : IInputReceivedHandler
    {
        public int Priority => 0;

        public async ValueTask<InputReceivedResult> HandleAsync(
            InputReceivedPayload payload,
            CancellationToken cancellationToken
        )
        {
            if (!string.Equals(payload.Text, "late", StringComparison.Ordinal))
            {
                return new InputReceivedResult.PassThrough();
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return new InputReceivedResult.PassThrough();
        }
    }
}

using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The mid-turn failure surface (issue #338): a continuation model request that fails after tool
/// results joined the turn must say so — the same <c>run failed:</c> notice a first-request
/// failure gets — and return to a usable input, instead of leaving the screen at its last state.
/// </summary>
public sealed class InteractiveSessionTurnFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_continuation_request_failure_after_tool_output_shows_the_run_failed_notice(
        bool fullscreen
    )
    {
        using var host = new InteractiveSessionHost(
            launch: fullscreen ? new LaunchOptions { Fullscreen = true } : new LaunchOptions(),
            configureHarness: options => options with { RetryBaseDelay = TimeSpan.Zero }
        );
        File.WriteAllText(host.Temp.File("a.txt"), "contents");
        host.Client.Enqueue(
            Scripts.Call("call-1", "read", Scripts.Args(("path", "a.txt"))),
            Scripts.ToolCalls()
        );
        host.Client.EnqueueFailure(new InvalidOperationException("fixture request mismatch"));
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        string scrollback = await host.WaitForScrollbackAsync("\u2713 read");
        Assert.Contains("a.txt", scrollback, StringComparison.Ordinal);

        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("run failed", StringComparison.Ordinal);
        });

        // The turn is over: the tool result is in, the error notice is visible and the session
        // accepts the next input.
        Assert.True(
            host.Frames.Contains("run failed", StringComparison.Ordinal),
            $"running={host.Session.IsRunning}, last={host.LastFrame.Replace('\n', '|')}"
        );
        Assert.False(host.Session.IsRunning);

        // Full-screen uses the same quit keys as the inline session (Ctrl+D twice); both modes
        // must leave the session usable for a normal quit after a failed turn.
        if (fullscreen)
        {
            host.Console.SendCtrlD();
            await host.WaitForFrameAsync("press Ctrl+D again to quit");
            host.Console.SendCtrlD();
            await run;
        }
        else
        {
            host.Console.SendText("/quit");
            host.Console.SendEnter();
            await run;
        }
    }

    [Fact]
    public async Task A_turn_kill_that_the_harness_cannot_name_still_shows_the_run_failed_notice()
    {
        // A fault in the session's own event pipe — an exception in the app's event seam, the
        // exact shape of a throw that surfaced somewhere the run could not name it (issue #338's
        // "the throw surface differed per run") — must not end the turn silently: the session
        // shows the same "run failed:" notice and returns to a usable input.
        using var host = new InteractiveSessionHost(eventObserverSuffix: agentEvent =>
        {
            if (agentEvent is ToolCallResult)
            {
                throw new InvalidOperationException("pipe fault after tool output");
            }
        });
        File.WriteAllText(host.Temp.File("a.txt"), "contents");
        host.Client.Enqueue(
            Scripts.Call("call-1", "read", Scripts.Args(("path", "a.txt"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("run failed", StringComparison.Ordinal);
        });

        Assert.True(
            host.Frames.Contains("run failed", StringComparison.Ordinal),
            $"running={host.Session.IsRunning}, last={host.LastFrame.Replace('\n', '|')}"
        );
        Assert.False(host.Session.IsRunning);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_first_request_failure_shows_the_run_failed_notice()
    {
        using var host = new InteractiveSessionHost(configureHarness: options =>
            options with
            {
                RetryBaseDelay = TimeSpan.Zero,
            }
        );
        host.Client.EnqueueFailure(new InvalidOperationException("fixture request mismatch"));
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("run failed", StringComparison.Ordinal);
        });

        Assert.True(
            host.Frames.Contains("run failed", StringComparison.Ordinal),
            $"running={host.Session.IsRunning}, last={host.LastFrame.Replace('\n', '|')}"
        );
        Assert.False(host.Session.IsRunning);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}

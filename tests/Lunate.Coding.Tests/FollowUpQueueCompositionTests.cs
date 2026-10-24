using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The production composition proof: a slow turn with two queued follow-ups drains in order, one
/// run at a time, through the real interactive host and the normal submission path.
/// </summary>
public sealed class FollowUpQueueCompositionTests
{
    [Fact]
    public async Task Two_queued_follow_ups_run_in_order_after_a_slow_turn_without_overlap()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("slow answer"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("first answer"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("second answer"), Scripts.Stop());
        host.Client.Gate(1);
        host.Client.Gate(2);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("start the slow turn");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        host.Console.SendText("first follow-up");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "first follow-up");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedFollowUpCount == 1);

        host.Console.SendText("second follow-up");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "second follow-up");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedFollowUpCount == 2);
        Assert.Equal(string.Empty, host.Session.Input.Text);

        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);
        Assert.Equal(2, host.Client.Requests.Count);
        Assert.Equal("first follow-up", host.Client.Requests[1][^1].Text);
        Assert.Equal(1, host.Session.QueuedFollowUpCount);

        host.Client.Release(2);
        await host.Client.WaitForCallAsync(3);
        Assert.Equal(3, host.Client.Requests.Count);
        Assert.Equal("second follow-up", host.Client.Requests[2][^1].Text);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        Assert.Equal(
            ["start the slow turn", "first follow-up", "second follow-up"],
            host.Client.Requests.Select(request => request[^1].Text)
        );
        Assert.Equal(0, host.Session.QueuedFollowUpCount);

        // Ordering needs one frame: wait until the later echo has committed, then compare.
        string scrollback = await host.WaitForScrollbackAsync("> second follow-up");
        int firstEcho = scrollback.IndexOf("> first follow-up", StringComparison.Ordinal);
        int secondEcho = scrollback.IndexOf("> second follow-up", StringComparison.Ordinal);
        Assert.True(firstEcho >= 0, "the first follow-up echo is missing");
        Assert.True(secondEcho > firstEcho, "the follow-up echoes are out of order");

        // No overlap: every top-level run finishes before the next one starts.
        string[] boundaries = host
            .Events.Where(agentEvent =>
                agentEvent.ParentRunId is null && agentEvent is RunStarted or RunFinished
            )
            .Select(agentEvent => agentEvent is RunStarted ? "start" : "finish")
            .ToArray();
        Assert.Equal(["start", "finish", "start", "finish", "start", "finish"], boundaries);

        host.Console.Complete();
        await run;
    }
}

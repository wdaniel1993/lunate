using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class FollowUpQueueSessionTests
{
    [Fact]
    public async Task Enter_while_a_turn_runs_queues_and_clears_the_line()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("second"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await QueueAsync(host, "run the tests", expected: 1);

        Assert.Equal(string.Empty, host.Session.Input.Text);
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("1 queued", StringComparison.Ordinal);
        });

        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);
        Assert.Equal("run the tests", host.Client.Requests[1][^1].Text);
        await host.WaitUntilAsync(() => host.Session.QueuedFollowUpCount == 0);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Alt_enter_while_idle_inserts_a_newline()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("one");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "one");
        host.Console.SendAltEnter();
        host.Console.SendText("two");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "one\ntwo");

        Assert.Equal(0, host.Session.QueuedFollowUpCount);
        Assert.Empty(host.Client.Requests);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Alt_enter_while_a_turn_runs_steers_and_queues_nothing()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("steered"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("steer");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "steer");
        host.Console.SendAltEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedSteeringCount == 1);

        Assert.Equal(0, host.Session.QueuedFollowUpCount);
        Assert.Equal(string.Empty, host.Session.Input.Text);

        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);
        Assert.Equal("steer", host.Client.Requests[1][^1].Text);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_head_of_the_queue_runs_after_the_turn_completes()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("second"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await QueueAsync(host, "run the tests", expected: 1);
        host.Client.Release(1);

        await host.Client.WaitForCallAsync(2);
        Assert.Equal("run the tests", host.Client.Requests[1][^1].Text);
        await host.WaitUntilAsync(() => host.Session.QueuedFollowUpCount == 0);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Queued_messages_run_in_order_one_at_a_time()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("second"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("third"), Scripts.Stop());
        host.Client.Gate(1);
        host.Client.Gate(2);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await QueueAsync(host, "second message", expected: 1);
        await QueueAsync(host, "third message", expected: 2);
        host.Client.Release(1);

        await host.Client.WaitForCallAsync(2);
        Assert.Equal("second message", host.Client.Requests[1][^1].Text);
        Assert.Equal(2, host.Client.Requests.Count);
        Assert.Equal(1, host.Session.QueuedFollowUpCount);
        Assert.True(host.Session.IsRunning);

        host.Client.Release(2);
        await host.Client.WaitForCallAsync(3);
        Assert.Equal("third message", host.Client.Requests[2][^1].Text);
        await host.WaitUntilAsync(() => host.Session.QueuedFollowUpCount == 0);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_failed_turn_still_runs_the_queued_follow_up()
    {
        using var host = new InteractiveSessionHost();
        host.Client.EnqueueFailure(new InvalidOperationException("provider exploded"));
        host.Client.Enqueue(Scripts.Text("second"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await QueueAsync(host, "after the failure", expected: 1);
        host.Client.Release(1);

        await host.Client.WaitForCallAsync(2);
        Assert.Equal("after the failure", host.Client.Requests[1][^1].Text);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        Assert.Equal(0, host.Session.QueuedFollowUpCount);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_cancelled_turn_keeps_the_queue_and_does_not_drain()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await QueueAsync(host, "after the cancel", expected: 1);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        Assert.Equal(1, host.Session.QueuedFollowUpCount);
        Assert.Single(host.Client.Requests);
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("1 queued", StringComparison.Ordinal);
        });

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_slash_command_is_refused_by_the_queue()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("/quit");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "/quit");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains(
                "slash command cannot be queued",
                StringComparison.Ordinal
            );
        });

        Assert.Equal(0, host.Session.QueuedFollowUpCount);
        Assert.Equal("/quit", host.Session.Input.Text);

        host.Client.Release(1);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_sixth_follow_up_is_refused_with_a_notice()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        for (var index = 0; index < FollowUpQueue.Capacity; index++)
        {
            await QueueAsync(host, $"message {index}", expected: index + 1);
        }

        host.Console.SendText("sixth");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "sixth");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("at most 5 messages", StringComparison.Ordinal);
        });

        Assert.Equal(FollowUpQueue.Capacity, host.Session.QueuedFollowUpCount);
        Assert.Equal("sixth", host.Session.Input.Text);

        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Enter_queues_an_attachment_with_the_text()
    {
        using var host = new InteractiveSessionHost();
        string image = host.Temp.File("shot.png");
        File.WriteAllBytes(image, ImageSamples.Png(2, 3));
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("second"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await QueueAsync(host, $"look @{image}", expected: 1);

        QueuedFollowUp queued = Assert.Single(host.Session.QueuedFollowUps);
        Assert.Equal($"look @{image}", queued.EditorText);
        Assert.Equal("look [image 2x3]", queued.Input.Text);
        Assert.Single(queued.Input.Images!);

        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);
        ChatMessage user = host.Client.Requests[1][^1];
        Assert.Contains(user.Contents, content => content is DataContent);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Alt_up_pulls_the_newest_entry_back_into_an_empty_line()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await QueueAsync(host, "one", expected: 1);
        await QueueAsync(host, "two", expected: 2);

        host.Console.SendAltUp();
        await host.WaitUntilAsync(() => host.Session.Input.Text == "two");

        Assert.Equal(1, host.Session.QueuedFollowUpCount);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Alt_up_walks_backwards_through_the_queue()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await QueueAsync(host, "one", expected: 1);
        await QueueAsync(host, "two", expected: 2);

        host.Console.SendAltUp();
        await host.WaitUntilAsync(() => host.Session.Input.Text == "two");
        host.Console.SendAltUp();
        await host.WaitUntilAsync(() => host.Session.Input.Text == "one");

        Assert.Equal(0, host.Session.QueuedFollowUpCount);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Alt_up_with_a_non_empty_line_refuses_and_changes_nothing()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await QueueAsync(host, "queued", expected: 1);
        host.Console.SendText("draft");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "draft");

        host.Console.SendAltUp();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("needs an empty line", StringComparison.Ordinal);
        });

        Assert.Equal("draft", host.Session.Input.Text);
        Assert.Equal(1, host.Session.QueuedFollowUpCount);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Escape_restores_the_whole_queue_in_order()
    {
        using var host = new InteractiveSessionHost();
        string image = host.Temp.File("shot.png");
        File.WriteAllBytes(image, ImageSamples.Png(2, 3));
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await QueueAsync(host, $"look @{image}", expected: 1);
        await QueueAsync(host, "second", expected: 2);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        host.Console.SendEscape();
        await host.WaitUntilAsync(
            () => host.Session.Input.Text == $"look @{image}\nsecond",
            TimeSpan.FromSeconds(2)
        );

        Assert.Equal(0, host.Session.QueuedFollowUpCount);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Escape_during_a_turn_cancels_and_leaves_the_queue_unchanged()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await QueueAsync(host, "after", expected: 1);

        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        Assert.Equal(1, host.Session.QueuedFollowUpCount);
        Assert.Equal(string.Empty, host.Session.Input.Text);

        host.Console.Complete();
        await run;
    }

    private static async Task QueueAsync(InteractiveSessionHost host, string text, int expected)
    {
        host.Console.SendText(text);
        await host.WaitUntilAsync(() => host.Session.Input.Text == text);
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedFollowUpCount == expected);
    }
}

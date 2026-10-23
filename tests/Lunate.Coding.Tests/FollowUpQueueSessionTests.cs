using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class FollowUpQueueSessionTests
{
    [Fact]
    public async Task A_queued_follow_up_shows_its_first_line_in_the_footer()
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
        await QueueAsync(host, "recognise me by my first line", expected: 1);

        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains(
                "recognise me by my first line",
                StringComparison.Ordinal
            );
        });

        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);
        host.Console.Complete();
        await run;
    }

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
    public async Task Alt_enter_while_a_turn_runs_inserts_a_newline_and_queues_nothing()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("one");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "one");
        host.Console.SendAltEnter();
        host.Console.SendText("two");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "one\ntwo");

        Assert.Equal(0, host.Session.QueuedFollowUpCount);
        Assert.Single(host.Client.Requests);

        host.Client.Release(1);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
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
    public async Task Escape_interrupts_the_turn_and_hands_over_to_the_queue_in_order()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("first queued"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("second queued"), Scripts.Stop());
        host.Client.Gate(1);
        host.Client.Gate(2);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await QueueAsync(host, "first queued", expected: 1);
        await QueueAsync(host, "second queued", expected: 2);
        host.Console.SendEscape();

        await host.Client.WaitForCallAsync(2);
        Assert.Equal("first queued", host.Client.Requests[1][^1].Text);
        Assert.Equal(2, host.Client.Requests.Count);
        Assert.True(host.Session.IsRunning);

        host.Client.Release(2);
        await host.Client.WaitForCallAsync(3);
        Assert.Equal("second queued", host.Client.Requests[2][^1].Text);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        Assert.Equal(0, host.Session.QueuedFollowUpCount);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_queue_is_not_written_to_the_session_file()
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
        await QueueAsync(host, "secret follow-up", expected: 1);

        string path = Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl"));
        await host.WaitUntilAsync(() =>
            File.Exists(path) && File.ReadAllText(path).Contains("\"hi\"", StringComparison.Ordinal)
        );
        Assert.DoesNotContain("secret follow-up", File.ReadAllText(path), StringComparison.Ordinal);

        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);
        Assert.Equal("secret follow-up", host.Client.Requests[1][^1].Text);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

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

        // Take the five entries back so the cancel has no queue to hand over to.
        host.Console.SendUp();
        await host.WaitUntilAsync(() => host.Session.QueuedFollowUpCount == 0);
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
    public async Task Up_takes_the_whole_queue_back_ahead_of_the_draft()
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

        host.Console.SendText("draft");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "draft");

        host.Console.SendUp();
        await host.WaitUntilAsync(() => host.Session.Input.Text == "one\ntwo\ndraft");

        Assert.Equal(0, host.Session.QueuedFollowUpCount);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Escape_with_nothing_running_leaves_the_queue_alone()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Session.FollowUps.TryEnqueue(new QueuedFollowUp("one", new UserInput("one")));
        host.Session.FollowUps.TryEnqueue(new QueuedFollowUp("two", new UserInput("two")));

        host.Console.SendEscape();
        host.Advance(66);

        Assert.Equal(2, host.Session.QueuedFollowUpCount);
        Assert.Equal(string.Empty, host.Session.Input.Text);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Escape_without_a_queue_cancels_the_turn()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        Assert.Empty(host.Client.Requests.Skip(1));
        Assert.Equal(string.Empty, host.Session.Input.Text);
        Assert.Equal(0, host.Session.QueuedFollowUpCount);

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

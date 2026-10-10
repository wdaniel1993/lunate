namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionSteeringTests
{
    [Fact]
    public async Task Enter_idle_starts_a_run_with_the_submitted_text()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Equal("hi", host.Client.Requests[0][^1].Text);
        Assert.Equal("user", host.Client.Requests[0][^1].Role.Value);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Injected_steering_is_echoed_to_scrollback_in_order()
    {
        using var host = new InteractiveSessionHost();
        File.WriteAllText(host.Temp.File("a.txt"), "contents");
        host.Client.Enqueue(
            Scripts.Call("call-1", "read", Scripts.Args(("path", "a.txt"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("steer one");
        host.Console.SendEnter();
        host.Console.SendText("steer two");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedSteeringCount == 2);
        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);

        await host.WaitUntilAsync(() =>
            host.ScrollbackText.Contains("» steer one", StringComparison.Ordinal)
            && host.ScrollbackText.Contains("» steer two", StringComparison.Ordinal)
        );
        Assert.True(
            host.ScrollbackText.IndexOf("» steer one", StringComparison.Ordinal)
                < host.ScrollbackText.IndexOf("» steer two", StringComparison.Ordinal)
        );

        host.Client.Release(2);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Steering_returned_to_the_input_line_is_never_echoed()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("unsent");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedSteeringCount == 1);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("unsent", StringComparison.Ordinal);
        });

        Assert.DoesNotContain("» unsent", host.ScrollbackText, StringComparison.Ordinal);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Auto_run_leftovers_are_not_echoed()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("steered"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("again");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedSteeringCount == 1);
        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        Assert.DoesNotContain("» again", host.ScrollbackText, StringComparison.Ordinal);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Enter_while_running_is_injected_before_the_next_model_call()
    {
        using var host = new InteractiveSessionHost();
        File.WriteAllText(host.Temp.File("a.txt"), "contents");
        host.Client.Enqueue(
            Scripts.Call("call-1", "read", Scripts.Args(("path", "a.txt"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("steer");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedSteeringCount == 1);
        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);

        IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> second = host.Client.Requests[1];
        Assert.Equal("steer", second[^1].Text);
        Assert.Equal("user", second[^1].Role.Value);
        Assert.Equal("tool", second[^2].Role.Value);

        host.Client.Release(2);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Escape_cancels_the_run_and_returns_queued_steering_to_the_input()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("unsent");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedSteeringCount == 1);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        host.Advance(33);

        Assert.Contains("unsent", host.LastFrame, StringComparison.Ordinal);
        Assert.Single(host.Client.Requests);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Escape_joins_multiple_queued_steering_messages_with_newlines()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("first");
        host.Console.SendEnter();
        host.Console.SendText("second");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedSteeringCount == 2);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> first", StringComparison.Ordinal)
                && host.LastFrame.Contains("second", StringComparison.Ordinal);
        });

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_normal_finish_auto_runs_the_leftover_steering()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("steered"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("again");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedSteeringCount == 1);
        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);

        Assert.Equal("again", host.Client.Requests[1][^1].Text);
        Assert.Equal("user", host.Client.Requests[1][^1].Role.Value);

        host.Client.Release(2);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task An_error_does_not_auto_run_and_returns_the_leftover_to_the_input()
    {
        using var host = new InteractiveSessionHost();
        File.WriteAllText(host.Temp.File("a.txt"), "contents");
        host.Client.Enqueue(
            Scripts.Call("call-1", "read", Scripts.Args(("path", "a.txt"))),
            Scripts.ToolCalls()
        );
        host.Client.EnqueueFailure(new InvalidOperationException("provider exploded"));
        host.Client.Gate(2);
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        host.Console.SendText("unsent");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedSteeringCount == 1);
        host.Client.Release(2);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        host.Advance(33);

        Assert.Contains("unsent", host.LastFrame, StringComparison.Ordinal);
        Assert.Equal(2, host.Client.Requests.Count);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_step_limit_does_not_auto_run_and_returns_the_leftover_to_the_input()
    {
        using var host = new InteractiveSessionHost(configureHarness: options =>
            options with
            {
                MaxSteps = 1,
            }
        );
        File.WriteAllText(host.Temp.File("a.txt"), "contents");
        host.Client.Enqueue(
            Scripts.Call("call-1", "read", Scripts.Args(("path", "a.txt"))),
            Scripts.ToolCalls()
        );
        host.Client.Gate(1);
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("unsent");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedSteeringCount == 1);
        host.Client.Release(1);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        host.Advance(33);

        Assert.Contains("unsent", host.LastFrame, StringComparison.Ordinal);
        Assert.Single(host.Client.Requests);
        host.Console.Complete();
        await run;
    }
}

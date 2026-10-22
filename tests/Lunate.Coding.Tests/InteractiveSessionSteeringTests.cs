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
        Func<string, string?> baseEnvironment = PrintModeTestSupport.Environment();
        using var host = new InteractiveSessionHost(environment: name =>
            name == "LUNATE_UNICODE" ? "1" : baseEnvironment(name)
        );
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
        host.Session.Steering.Enqueue("steer one");
        host.Session.Steering.Enqueue("steer two");
        Assert.Equal(2, host.Session.Steering.Count);
        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);

        await host.WaitUntilAsync(() =>
            host.ScrollbackText.Contains("» steer one", StringComparison.Ordinal)
            && host.ScrollbackText.Contains("» steer two", StringComparison.Ordinal)
        );
        Assert.Contains("> hi\n\n", host.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain("» hi", host.ScrollbackText, StringComparison.Ordinal);
        Assert.True(
            host.ScrollbackText.IndexOf("» steer one", StringComparison.Ordinal)
                < host.ScrollbackText.IndexOf("» steer two", StringComparison.Ordinal)
        );

        host.Client.Release(2);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Injected_steering_echoes_the_ascii_prefix_when_unicode_is_off()
    {
        Func<string, string?> baseEnvironment = PrintModeTestSupport.Environment();
        using var host = new InteractiveSessionHost(environment: name =>
            name is "LUNATE_ASCII" or "LUNATE_UNICODE" ? "1" : baseEnvironment(name)
        );
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
        host.Session.Steering.Enqueue("steer");
        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);

        await host.WaitUntilAsync(() =>
            host.ScrollbackText.Contains(">> steer", StringComparison.Ordinal)
        );
        string[] lines = host.ScrollbackText.Split('\n');
        Assert.Contains(">> steer", lines);
        Assert.Contains("> hi", lines);
        Assert.DoesNotContain("> steer", lines);
        Assert.DoesNotContain("» steer", host.ScrollbackText, StringComparison.Ordinal);

        host.Client.Release(2);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Steering_from_the_harness_api_is_injected_before_the_next_model_call()
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
        host.Session.Steering.Enqueue("steer");
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
    public async Task Leftover_steering_stays_queued_after_a_cancel_and_is_never_echoed()
    {
        Func<string, string?> baseEnvironment = PrintModeTestSupport.Environment();
        using var host = new InteractiveSessionHost(environment: name =>
            name == "LUNATE_UNICODE" ? "1" : baseEnvironment(name)
        );
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Session.Steering.Enqueue("unsent");
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        Assert.Equal(1, host.Session.Steering.Count);
        Assert.Equal(string.Empty, host.Session.Input.Text);
        Assert.DoesNotContain("» unsent", host.ScrollbackText, StringComparison.Ordinal);
        host.Console.Complete();
        await run;
    }
}

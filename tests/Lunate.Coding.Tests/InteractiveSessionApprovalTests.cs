using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionApprovalTests
{
    [Fact]
    public async Task Yes_approves_the_call_once()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(WriteCall("call-1", "note.txt", "hi"), Scripts.ToolCalls());
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);

        Assert.Equal("write", host.Session.PendingApproval!.ToolName);
        host.Console.SendText("y");
        await host.Client.WaitForCallAsync(2);

        Assert.Equal("hi", File.ReadAllText(host.Temp.File("note.txt")));
        Assert.Null(host.Session.PendingApproval);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Enter_denies_and_the_model_sees_the_denial()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(WriteCall("call-1", "note.txt", "hi"), Scripts.ToolCalls());
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);

        Assert.False(File.Exists(host.Temp.File("note.txt")));
        string result = Assert
            .Single(host.Client.Requests[1][^1].Contents.OfType<FunctionResultContent>())
            .Result!.ToString()!;
        Assert.Contains("Denied", result, StringComparison.Ordinal);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Always_remembers_the_tool_name_for_the_session()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(WriteCall("call-1", "a.txt", "one"), Scripts.ToolCalls());
        host.Client.Enqueue(WriteCall("call-2", "b.txt", "two"), Scripts.ToolCalls());
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        host.Console.SendText("a");

        // Reaching the third model call proves the second write ran without a prompt.
        await host.Client.WaitForCallAsync(3);
        Assert.Equal("one", File.ReadAllText(host.Temp.File("a.txt")));
        Assert.Equal("two", File.ReadAllText(host.Temp.File("b.txt")));
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_input_line_is_locked_while_the_prompt_is_open()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(WriteCall("call-1", "note.txt", "hi"), Scripts.ToolCalls());
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        host.Console.SendText("abc");
        host.Console.SendText("y");
        await host.Client.WaitForCallAsync(2);

        Assert.Equal(2, host.Client.Requests.Count);
        Assert.Equal(0, host.Session.QueuedSteeringCount);
        host.Console.Complete();
        await run;
        host.Advance(33);

        Assert.DoesNotContain("abc", host.LastFrame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CtrlC_still_quits_while_the_prompt_is_open()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(WriteCall("call-1", "note.txt", "hi"), Scripts.ToolCalls());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        host.Console.SendCtrlC();
        host.Console.SendCtrlC();
        await run;

        Assert.False(File.Exists(host.Temp.File("note.txt")));
        Assert.False(host.Session.IsRunning);
    }

    [Fact]
    public async Task Always_does_not_leak_into_a_new_session()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(WriteCall("call-1", "a.txt", "one"), Scripts.ToolCalls());
        host.Client.Enqueue(WriteCall("call-2", "b.txt", "two"), Scripts.ToolCalls());
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        host.Client.Enqueue(WriteCall("call-3", "c.txt", "three"), Scripts.ToolCalls());
        host.Client.Enqueue(Scripts.Text("done again"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        host.Console.SendText("a");

        // Reaching the third call proves the second write ran without a prompt.
        await host.Client.WaitForCallAsync(3);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.SendText("/new");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session");

        host.Console.SendText("go again");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        host.Console.SendText("y");
        await host.Client.WaitForCallAsync(5);

        Assert.Equal("two", File.ReadAllText(host.Temp.File("b.txt")));
        Assert.Equal("three", File.ReadAllText(host.Temp.File("c.txt")));
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Always_does_not_leak_into_a_resumed_session()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(WriteCall("call-1", "a.txt", "one"), Scripts.ToolCalls());
        host.Client.Enqueue(WriteCall("call-2", "b.txt", "two"), Scripts.ToolCalls());
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        host.Client.Enqueue(WriteCall("call-3", "c.txt", "three"), Scripts.ToolCalls());
        host.Client.Enqueue(Scripts.Text("done again"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        host.Console.SendText("a");
        await host.Client.WaitForCallAsync(3);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        host.Console.SendText("/resume");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select session");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "resumed ");

        host.Console.SendText("go again");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        host.Console.SendText("y");
        await host.Client.WaitForCallAsync(5);

        Assert.Equal("three", File.ReadAllText(host.Temp.File("c.txt")));
        host.Console.Complete();
        await run;
    }

    private static ChatResponseUpdate WriteCall(string callId, string path, string content) =>
        Scripts.Call(callId, "write", Scripts.Args(("path", path), ("content", content)));
}

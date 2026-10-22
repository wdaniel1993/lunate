using Lunate.Agent;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionBashModeTests
{
    [Fact]
    public async Task A_bang_at_the_start_of_an_empty_input_enters_bash_mode()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("!");
        await host.WaitUntilAsync(() => host.Session.IsBashMode);

        Assert.Equal(string.Empty, host.Session.Input.Text);
        Assert.False(host.Session.IsBashSilent);
        host.Advance(33);
        Assert.Contains("! ", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_bang_inside_the_text_stays_literal()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        const string text = "explain the ! operator";
        host.Console.SendText(text);
        await host.WaitUntilAsync(() => host.Session.Input.Text == text);

        Assert.False(host.Session.IsBashMode);

        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        ChatMessage prompt = host.Client.Requests[0][^1];
        Assert.Equal(text, Assert.Single(prompt.Contents.OfType<TextContent>()).Text);
        Assert.DoesNotContain(host.Events, agentEvent => agentEvent is ToolCallStart);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_double_bang_enters_the_silent_form()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("!!");
        await host.WaitUntilAsync(() => host.Session.IsBashMode && host.Session.IsBashSilent);

        Assert.Equal(string.Empty, host.Session.Input.Text);
        host.Advance(33);
        Assert.Contains("! ", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Escape_leaves_bash_mode_and_restores_the_prefix()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("!git status");
        await host.WaitUntilAsync(() =>
            host.Session.IsBashMode && host.Session.Input.Text == "git status"
        );

        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsBashMode);

        Assert.Equal("!git status", host.Session.Input.Text);
        host.Advance(33);
        Assert.Contains("> !git status", host.LastFrame, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);
        Assert.DoesNotContain(host.Events, agentEvent => agentEvent is ToolCallStart);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Enter_on_a_bare_bang_leaves_without_running()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("!");
        await host.WaitUntilAsync(() => host.Session.IsBashMode);

        host.Console.SendEnter();
        await host.WaitUntilAsync(() => !host.Session.IsBashMode);

        Assert.Equal(string.Empty, host.Session.Input.Text);
        Assert.Empty(host.Client.Requests);
        Assert.DoesNotContain(host.Events, agentEvent => agentEvent is ToolCallStart);

        host.Console.Complete();
        await run;
    }
}

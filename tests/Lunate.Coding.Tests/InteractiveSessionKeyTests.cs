using Lunate.Tui;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionKeyTests
{
    [Fact]
    public async Task Up_and_down_navigate_the_history_on_the_input_loop()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("hello");
        host.Console.SendEnter();

        // The committed scrollback is the race-free completion signal: when the run ends, the
        // live tail is cleared right after the commit, so a frame-based wait would race the
        // commit and lose deterministically on the slower Windows runners.
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.ScrollbackText.Contains("ok", StringComparison.Ordinal);
        });

        host.Console.Send(new KeyEvent(KeyKind.Up, null, false, false, false));
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> hello", StringComparison.Ordinal);
        });

        host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.LastFrame.Contains("> hello", StringComparison.Ordinal);
        });

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task CtrlC_clears_non_empty_input_and_a_second_empty_press_quits()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("abc");
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> abc", StringComparison.Ordinal);
        });

        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.LastFrame.Contains("abc", StringComparison.Ordinal);
        });

        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("press Ctrl+C again to quit", StringComparison.Ordinal);
        });

        host.Console.SendCtrlC();
        await run;
    }

    [Fact]
    public async Task The_quit_window_expires_on_virtual_time()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("press Ctrl+C again to quit", StringComparison.Ordinal);
        });

        host.Advance(2100);
        host.Console.SendCtrlC();
        host.Console.SendCtrlC();
        await run;

        Assert.False(host.Session.IsRunning);
    }

    [Fact]
    public async Task Escape_while_idle_does_nothing()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("draft");
        host.Console.SendEscape();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> draft", StringComparison.Ordinal);
        });

        Assert.Empty(host.Client.Requests);
        Assert.False(host.Session.IsRunning);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task CtrlC_interrupts_a_running_turn_without_touching_the_draft()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        Assert.Single(host.Client.Requests);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task CtrlD_quits_on_a_second_press_from_an_empty_prompt()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendCtrlD();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("press Ctrl+D again to quit", StringComparison.Ordinal);
        });

        host.Console.SendCtrlD();
        await run;
    }

    [Fact]
    public async Task CtrlD_does_not_quit_while_the_prompt_holds_text()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("draft");
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> draft", StringComparison.Ordinal);
        });

        host.Console.SendCtrlD();
        host.Console.SendCtrlD();
        host.Advance(33);

        Assert.False(run.IsCompleted);
        Assert.Contains("> draft", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task CtrlL_redraws_the_prompt_without_losing_the_input()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("draft");
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> draft", StringComparison.Ordinal);
        });

        host.Console.SendCtrlL();
        host.Advance(33);

        Assert.Contains("> draft", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("Select model", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }
}

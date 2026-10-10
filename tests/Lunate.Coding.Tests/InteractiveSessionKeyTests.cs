using Lunate.Tui;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionKeyTests
{
    [Fact]
    public async Task Up_and_down_navigate_the_history_on_the_input_loop()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        host.Client.Gate(2);
        Task run = host.RunAsync();

        host.Console.SendText("hello");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.Frames.Contains("ok", StringComparison.Ordinal);
        });

        // The streamed tail must be provably painted before the run may end: the run's end
        // commits the tail and clears it, and without the gate the paint tick would race the
        // commit (deterministically lost on the slower Windows runners).
        host.Client.Release(2);

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
}

using System.Text.Json;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionBashHistoryTests
{
    [Fact]
    public async Task A_bash_line_is_recalled_by_up_in_bash_mode_only()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await RunBashLineAsync(host, "!echo one");

        host.Console.Send(new KeyEvent(KeyKind.Up, null, false, false, false));
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);

        host.Console.SendText("!");
        await host.WaitUntilAsync(() => host.Session.IsBashMode);
        host.Console.Send(new KeyEvent(KeyKind.Up, null, false, false, false));
        await host.WaitUntilAsync(() => host.Session.Input.Text == "echo one");

        host.Advance(33);
        Assert.Contains("! echo one", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_prompt_is_not_recalled_in_bash_mode()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hello");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        host.Console.SendText("!");
        await host.WaitUntilAsync(() => host.Session.IsBashMode);
        host.Console.Send(new KeyEvent(KeyKind.Up, null, false, false, false));
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);

        Assert.True(host.Session.IsBashMode);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Bash_lines_leave_the_prompt_history_file_unchanged()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await RunBashLineAsync(host, "!echo one");
        Assert.False(File.Exists(host.Temp.File("history")));

        host.Console.SendText("hello");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        string line = Assert.Single(File.ReadAllLines(host.Temp.File("history")));
        Assert.Equal("hello", JsonSerializer.Deserialize<string>(line));
        Assert.DoesNotContain("echo one", line, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    private static async Task RunBashLineAsync(InteractiveSessionHost host, string line)
    {
        host.Console.SendText(line);
        await host.WaitUntilAsync(() => host.Session.IsBashMode);
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        host.Console.SendText("y");
        await host.WaitForScrollbackAsync(
            text =>
                text.Contains("exit 0", StringComparison.Ordinal) && !host.Session.IsBashRunning,
            "the bash result once the shell exits"
        );
    }
}

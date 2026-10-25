using Lunate.Agent;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionBashExecutionTests
{
    [Fact]
    public async Task A_bash_line_runs_in_the_session_workspace_and_commits_one_block()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("!echo hi > bash-mode.txt");
        await host.WaitUntilAsync(() => host.Session.IsBashMode);
        host.Console.SendEnter();

        await host.WaitForApprovalAsync();
        Assert.Equal("bash", host.Session.PendingApproval!.ToolName);
        Assert.False(File.Exists(host.Temp.File("bash-mode.txt")));

        host.Console.SendText("y");
        // The compact block is the marker; the finished shell is the event. One snapshot then
        // answers all three assertions.
        string scrollback = await host.WaitForScrollbackAsync(
            text =>
                text.Contains("exit 0", StringComparison.Ordinal) && !host.Session.IsBashRunning,
            "the bash block once the shell exits"
        );

        Assert.Equal("hi\n", File.ReadAllText(host.Temp.File("bash-mode.txt")));
        Assert.Contains("\u2713 bash", scrollback, StringComparison.Ordinal);
        Assert.Contains("exit 0", scrollback, StringComparison.Ordinal);
        Assert.Contains("0.0s", scrollback, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);
        Assert.Contains(
            host.Events,
            agentEvent =>
                agentEvent is ToolCallStart { ToolName: "bash" } start
                && start.CallId.StartsWith("bash-", StringComparison.Ordinal)
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_non_zero_exit_reaches_the_block_header()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("!exit 7");
        await host.WaitUntilAsync(() => host.Session.IsBashMode);
        host.Console.SendEnter();
        await host.WaitForApprovalAsync();
        host.Console.SendText("y");
        string scrollback = await host.WaitForScrollbackAsync(
            text =>
                text.Contains("exit 7", StringComparison.Ordinal) && !host.Session.IsBashRunning,
            "the failed bash block once the shell exits"
        );

        Assert.Contains(
            "\u2717 bash  exit 7 \u00b7 exit 7 \u00b7 0.0s",
            scrollback,
            StringComparison.Ordinal
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_block_duration_is_measured_on_the_session_clock()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("!echo timed");
        await host.WaitUntilAsync(() => host.Session.IsBashMode);
        host.Console.SendEnter();
        await host.WaitForApprovalAsync();
        host.Advance(400);
        host.Console.SendText("y");
        string scrollback = await host.WaitForScrollbackAsync(
            text =>
                text.Contains("echo timed", StringComparison.Ordinal)
                && !host.Session.IsBashRunning,
            "the bash block once the shell exits"
        );

        Assert.Contains(
            "\u2713 bash  echo timed \u00b7 exit 0 \u00b7 0.4s",
            scrollback,
            StringComparison.Ordinal
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_denied_bash_line_does_not_run()
    {
        using var host = new InteractiveSessionHost();
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "toolDetails": "expanded"
            }
            """
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("!echo denied > denied.txt");
        await host.WaitUntilAsync(() => host.Session.IsBashMode);
        host.Console.SendEnter();
        await host.WaitForApprovalAsync();
        host.Console.SendEnter();

        string scrollback = await host.WaitForScrollbackAsync(
            text =>
                text.Contains("Denied", StringComparison.Ordinal) && !host.Session.IsBashRunning,
            "the denied bash block once the shell stops"
        );

        Assert.False(File.Exists(host.Temp.File("denied.txt")));
        Assert.Contains("\u2717 bash", scrollback, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_cancelled_command_reports_cancellation_not_a_partial_run()
    {
        using var host = new InteractiveSessionHost();
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "toolDetails": "expanded"
            }
            """
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("!" + LongRunningCommand());
        await host.WaitUntilAsync(() => host.Session.IsBashMode);
        host.Console.SendEnter();
        await host.WaitForApprovalAsync();
        host.Console.SendText("y");
        await host.WaitUntilAsync(() => host.Session.PendingApproval is null);

        host.Console.SendEscape();
        string scrollback = await host.WaitForScrollbackAsync(
            text =>
                text.Contains("cancelled", StringComparison.Ordinal) && !host.Session.IsBashRunning,
            "the cancelled bash block once the shell stops"
        );

        Assert.Contains("\u2717 bash", scrollback, StringComparison.Ordinal);
        Assert.DoesNotContain("exit code", scrollback, StringComparison.Ordinal);
        host.Advance(33);
        Assert.Contains("cancelled", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    private static string LongRunningCommand() =>
        OperatingSystem.IsWindows()
            ? "powershell.exe -NoProfile -Command \"Start-Sleep 300\""
            : "sleep 300";
}

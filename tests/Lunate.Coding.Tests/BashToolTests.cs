using System.Globalization;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class BashToolTests
{
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());

    [Fact]
    public void Tool_shape_is_pinned()
    {
        using var temp = new TempDirectory();
        var tool = new BashTool(new Workspace(temp.Root));

        Assert.Equal("bash", tool.Name);
        Assert.Equal(ToolRisk.Execute, tool.Risk);
        var schema = tool.ParametersSchema;
        Assert.Contains(
            schema.GetProperty("required").EnumerateArray(),
            element => element.GetString() == "command"
        );
        Assert.True(schema.GetProperty("properties").TryGetProperty("command", out _));
    }

    [Fact]
    public void Description_names_the_resolved_shell()
    {
        using var temp = new TempDirectory();
        var resolver = new ShellResolver();
        var shell = resolver.Resolve();
        Assert.NotNull(shell);

        var tool = new BashTool(new Workspace(temp.Root), resolver);

        Assert.Contains(shell.DisplayName, tool.Description);
        Assert.Contains("stdin is closed", tool.Description);
    }

    [Fact]
    public async Task Echo_round_trips_with_exit_code_zero()
    {
        using var temp = new TempDirectory();

        var result = await RunAsync(temp, "echo hello");

        Assert.False(result.IsError);
        Assert.Equal("hello\nexit code: 0", result.Output);
    }

    [Fact]
    public async Task A_non_zero_exit_is_an_error_with_the_code_in_the_text()
    {
        using var temp = new TempDirectory();

        var result = await RunAsync(temp, "echo oops >&2; exit 7");

        Assert.True(result.IsError);
        Assert.Equal("stderr:\noops\nexit code: 7", result.Output);
    }

    [Fact]
    public async Task Exit_code_zero_travels_in_the_details_payload()
    {
        using var temp = new TempDirectory();

        var result = await RunAsync(temp, "echo hello");

        Assert.Equal(new BashDetails(0), result.Details);
    }

    [Fact]
    public async Task A_non_zero_exit_code_travels_in_the_details_payload()
    {
        using var temp = new TempDirectory();

        var result = await RunAsync(temp, "exit 7");

        Assert.Equal(new BashDetails(7), result.Details);
    }

    [Fact]
    public async Task Stdout_and_stderr_are_separated()
    {
        using var temp = new TempDirectory();

        var result = await RunAsync(temp, "echo out; echo err >&2");

        Assert.False(result.IsError);
        Assert.Equal("out\nstderr:\nerr\nexit code: 0", result.Output);
    }

    [Fact]
    public async Task Utf8_output_round_trips()
    {
        using var temp = new TempDirectory();

        var result = await RunAsync(temp, "printf 'gr\u00fc\u00dfe \u4f60\u597d \U0001f389\\n'");

        Assert.False(result.IsError);
        Assert.Equal("gr\u00fc\u00dfe \u4f60\u597d \U0001f389\nexit code: 0", result.Output);
    }

    [Fact]
    public async Task Commands_run_in_the_canonical_workspace_root()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);

        if (!OperatingSystem.IsWindows())
        {
            var result = await RunAsync(workspace, "pwd");

            Assert.False(result.IsError);
            Assert.Equal(workspace.WorktreeRoot + "\nexit code: 0", result.Output);
            return;
        }

        var windowsResult = await RunAsync(workspace, "echo marker > cwd-probe.txt");

        Assert.False(windowsResult.IsError);
        Assert.Equal(
            "marker",
            File.ReadAllText(Path.Combine(workspace.WorktreeRoot, "cwd-probe.txt")).Trim()
        );
    }

    [Fact]
    public async Task Stdinput_is_closed()
    {
        using var temp = new TempDirectory();

        var result = await RunAsync(temp, "cat; echo after", WaitBudgets.CommandRun);

        Assert.False(result.IsError);
        Assert.Equal("after\nexit code: 0", result.Output);
    }

    [Fact]
    public async Task Output_over_the_cap_ends_with_the_marker()
    {
        using var temp = new TempDirectory();

        var result = await RunAsync(temp, "yes 0123456789 2>/dev/null | head -c 1200000");

        Assert.False(result.IsError);
        Assert.Contains(BashTool.CapMarker, result.Output);
        Assert.True(
            result.Output.Length < 1_000_100,
            $"captured output should stay bounded, was {result.Output.Length} characters"
        );
    }

    [Fact]
    public async Task Output_over_the_cap_completes_while_stderr_stays_open()
    {
        using var temp = new TempDirectory();

        var result = await RunAsync(
            temp,
            "(sleep 2) & yes 'filler' | head -c 1200000; wait",
            WaitBudgets.CommandRun
        );

        Assert.False(result.IsError);
        Assert.Contains(BashTool.CapMarker, result.Output);
    }

    [Fact]
    public async Task An_empty_result_shows_only_the_exit_code()
    {
        using var temp = new TempDirectory();

        var result = await RunAsync(temp, "true");

        Assert.False(result.IsError);
        Assert.Equal("exit code: 0", result.Output);
    }

    [Fact]
    public async Task A_timeout_kills_the_process_tree()
    {
        using var temp = new TempDirectory();
        var pidFile = temp.File("grandchild.pid");
        DateTime commandStarted = DateTime.Now;

        // The timeout that makes the kill fire is a tree-kill budget, never a wait: it has to
        // outlast a cold PowerShell start on a loaded runner (the spawn-heavy suite starves
        // process startup, which is why ten seconds was not enough) while staying short enough
        // that the test spends its time asserting the kill, not waiting for a timeout.
        var timeout = WaitBudgets.TreeKillTimeout;

        // The grandchild is pinned while the command is still running. Read after the kill, the
        // number in the file may already name a process this test never started; see
        // ChildProcessProbe for the failure that came from reading it late.
        using var window = new CancellationTokenSource();
        Task<ToolResult> execution = RunAsync(temp, TreeCommand(), timeout);
        var watched = ChildProcessProbe.AwaitWrittenAsync(
            pidFile,
            commandStarted,
            window.Token,
            ToolWatch(temp, execution)
        );
        var result = await execution;
        window.Cancel();
        var grandchild = await watched;

        Assert.True(result.IsError);
        Assert.Null(result.Details);
        Assert.Contains(
            $"timed out after {timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s; process tree killed",
            result.Output
        );
        await ChildProcessProbe.AssertEventuallyDeadAsync(grandchild);
    }

    [Fact]
    public async Task Cancellation_kills_the_tree_and_propagates()
    {
        using var temp = new TempDirectory();
        var pidFile = temp.File("grandchild.pid");
        using var cts = new CancellationTokenSource();
        // The tool's own command timeout is a guard here, not the subject: the tree kill the test
        // observes has to be the one its cancellation fires, so the guard sits above the spawn
        // window the pin probe waits and cannot end the command while the window is open.
        var tool = new BashTool(new Workspace(temp.Root), null, WaitBudgets.TreeKillGuard);
        DateTime commandStarted = DateTime.Now;

        // Pinned while the command still runs, and the window closes if the run ends first: a pid
        // read after the kill no longer names the process the kill ended. The normal path here is
        // the pin landing before the cancellation, so the run cannot end first unless it failed.
        using var window = new CancellationTokenSource();
        var execution = tool.ExecuteAsync(CommandArgs(TreeCommand()), Context, cts.Token);
        var watched = ChildProcessProbe.AwaitWrittenAsync(
            pidFile,
            commandStarted,
            window.Token,
            ToolWatch(temp, execution)
        );
        if (await Task.WhenAny(watched, execution) == execution)
        {
            window.Cancel();
            await execution;
        }

        var grandchild = await watched;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        await ChildProcessProbe.AssertEventuallyDeadAsync(grandchild);
    }

    [Fact]
    public async Task A_missing_shell_is_a_clear_error()
    {
        using var temp = new TempDirectory();
        var probe = new FakeShellProbe { IsWindows = false };
        var tool = new BashTool(new Workspace(temp.Root), new ShellResolver(null, probe.Build()));

        var result = await tool.ExecuteAsync(
            CommandArgs("echo hello"),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("no shell found", result.Output);
    }

    [Fact]
    public async Task A_shell_that_cannot_start_is_a_spawn_error()
    {
        using var temp = new TempDirectory();
        var missing = Path.Combine(temp.Root, "missing-shell");
        var tool = new BashTool(new Workspace(temp.Root), new ShellResolver(missing));

        var result = await tool.ExecuteAsync(
            CommandArgs("echo hello"),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("could not start", result.Output);
        Assert.Contains(missing, result.Output);
    }

    [Theory]
    [InlineData("{}", "command is required and must be a string")]
    [InlineData("""{"command":5}""", "command is required and must be a string")]
    [InlineData("[]", "arguments must be a JSON object")]
    public async Task Malformed_arguments_are_errors(string arguments, string expected)
    {
        using var temp = new TempDirectory();
        var tool = new BashTool(new Workspace(temp.Root));

        var result = await tool.ExecuteAsync(Args(arguments), Context, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(expected, result.Output);
    }

    private static string TreeCommand() =>
        OperatingSystem.IsWindows()
            ? "powershell.exe -NoProfile -Command 'Set-Content -Path grandchild.pid -Value $PID; Start-Sleep 300' & sleep 300"
            : "sh -c 'echo $$ > grandchild.pid; exec sleep 300' & sleep 300";

    private static JsonElement Args(string arguments) =>
        JsonDocument.Parse(arguments).RootElement.Clone();

    private static JsonElement CommandArgs(string command) =>
        Args(JsonSerializer.Serialize(new { command }));

    /// <summary>
    /// The tool run's own state, for a probe failure that has to say whether the shell that should
    /// write the pid ever ran and what the tool ended with.
    /// </summary>
    private static CommandUnderTest ToolWatch(TempDirectory temp, Task<ToolResult> run) =>
        new(temp.Root)
        {
            Run = run,
            DescribeOutcome = () => run.Result.Output.ReplaceLineEndings(" ").Trim(),
        };

    private static async Task<ToolResult> RunAsync(
        TempDirectory temp,
        string command,
        TimeSpan? timeout = null
    ) => await RunAsync(new Workspace(temp.Root), command, timeout);

    private static async Task<ToolResult> RunAsync(
        Workspace workspace,
        string command,
        TimeSpan? timeout = null
    )
    {
        var tool = new BashTool(workspace, null, timeout);

        return await tool.ExecuteAsync(CommandArgs(command), Context, CancellationToken.None);
    }
}

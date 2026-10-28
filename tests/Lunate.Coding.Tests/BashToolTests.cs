using System.ComponentModel;
using System.Diagnostics;
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

        var result = await RunAsync(temp, "cat; echo after", TimeSpan.FromSeconds(10));

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
            TimeSpan.FromSeconds(30)
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

        // Windows needs a longer window: PowerShell's cold start on a loaded runner can exceed
        // the short Unix timeout, so the pid would never be written before the tree is killed.
        // 10s was not enough under CI load (the spawn-heavy suite starves process startup); the
        // window only lengthens the test, it does not weaken the kill assertion.
        var timeout = OperatingSystem.IsWindows()
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromSeconds(2);

        var result = await RunAsync(temp, TreeCommand(), timeout);

        Assert.True(result.IsError);
        Assert.Null(result.Details);
        Assert.Contains(
            $"timed out after {timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s; process tree killed",
            result.Output
        );
        var pid = await WaitForPidAsync(pidFile);
        await AssertEventuallyDeadAsync(pid);
    }

    [Fact]
    public async Task Cancellation_kills_the_tree_and_propagates()
    {
        using var temp = new TempDirectory();
        var pidFile = temp.File("grandchild.pid");
        using var cts = new CancellationTokenSource();
        var tool = new BashTool(new Workspace(temp.Root), null, TimeSpan.FromSeconds(60));

        var execution = tool.ExecuteAsync(CommandArgs(TreeCommand()), Context, cts.Token);
        var pid = await WaitForPidAsync(pidFile);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        await AssertEventuallyDeadAsync(pid);
    }

    [Fact]
    public void A_survivor_is_judged_by_start_time_not_by_the_pid_alone()
    {
        // The kill assertions end when the pid is gone — or, since the wait pins the process, when
        // the pid now names a different process. Both halves of that rule are load-bearing: a
        // start-time read that never matched would make the wait pass vacuously and a child the
        // tool leaked would go unseen, and a read that only compared pids would fail on a runner
        // that recycled the number. The mismatch is simulated by shifting the captured stamp: a
        // process wearing the same pid but created a second later is exactly what recycling hands
        // back.
        if (TryRead(Environment.ProcessId) is not { } self)
        {
            Assert.Fail("the test process must be readable from its own pid");
            return;
        }
        Assert.True(IsStillAlive(Environment.ProcessId, self), "a live process read as alive");
        Assert.False(
            IsStillAlive(Environment.ProcessId, (self.Name, self.StartedAt.AddSeconds(1))),
            "another process wearing the pid read as the survivor"
        );
        Assert.False(IsStillAlive(int.MaxValue, self), "a pid nobody holds read as the survivor");
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

    private static async Task<int> WaitForPidAsync(string pidFile)
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            if (File.Exists(pidFile))
            {
                try
                {
                    var text = File.ReadAllText(pidFile).Trim();
                    if (
                        int.TryParse(
                            text,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var pid
                        )
                    )
                    {
                        return pid;
                    }
                }
                catch (IOException)
                {
                    // The writer may still hold the file briefly (Windows sharing); retry.
                }
                catch (UnauthorizedAccessException)
                {
                    // The file exists but is momentarily unreadable; retry.
                }
            }

            await Task.Delay(100);
        }

        Assert.Fail($"no pid was written to {pidFile} within 60 seconds");
        return 0;
    }

    /// <summary>
    /// Waits until the process the tool killed is gone. A pid is not a name for that process: the
    /// Windows runner recycles pids while the suite keeps spawning children, and a recycled number
    /// reads as a survivor the tool never left behind — no budget can fix that, it only waits
    /// longer for a process that will never die. The start time is pinned while the process is
    /// still ours, so the question is "did <em>that</em> process die?" and the budget only has to
    /// cover the teardown itself (<see cref="WaitBudgets.ProcessExit"/> says why it is wide there).
    /// </summary>
    private static async Task AssertEventuallyDeadAsync(int pid)
    {
        if (TryRead(pid) is not { } killed)
        {
            // Nothing to compare: the pid held no process by the time the test read it.
            return;
        }

        DateTime deadline = DateTime.UtcNow + WaitBudgets.ProcessExit;
        while (IsStillAlive(pid, killed))
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"pid {pid} ({killed.Name}, started {killed.StartedAt:HH:mm:ss.fff}) was still alive {WaitBudgets.ProcessExit.TotalSeconds:0} seconds after the tree kill"
                    )
                );
            }

            await Task.Delay(100);
        }
    }

    /// <summary>
    /// Alive means a process still holds this pid <em>and</em> it started when ours did: the start
    /// time is the creation stamp, so a recycled pid cannot match it. A pid nobody holds — or one
    /// this process may no longer inspect — is not the survivor the kill under test left behind.
    /// </summary>
    private static bool IsStillAlive(int pid, (string Name, DateTime StartedAt) killed) =>
        TryRead(pid) is { } current && current.StartedAt == killed.StartedAt;

    /// <summary>
    /// The name and creation stamp behind a pid, or null when no process we may inspect holds it
    /// any more — which is the answer a killed child is expected to give.
    /// </summary>
    private static (string Name, DateTime StartedAt)? TryRead(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.HasExited ? null : (process.ProcessName, process.StartTime);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            // Exited between the lookup and the read, or a pid this process may not inspect:
            // neither is a survivor of the kill under test.
            return null;
        }
    }

    private static JsonElement Args(string arguments) =>
        JsonDocument.Parse(arguments).RootElement.Clone();

    private static JsonElement CommandArgs(string command) =>
        Args(JsonSerializer.Serialize(new { command }));

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

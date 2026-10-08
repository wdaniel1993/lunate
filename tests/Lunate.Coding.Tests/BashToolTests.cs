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
        Assert.Contains(
            "... [output capped at 1000000 characters; the rest was discarded]",
            result.Output
        );
        Assert.True(
            result.Output.Length < 1_000_100,
            $"captured output should stay bounded, was {result.Output.Length} characters"
        );
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

        var result = await RunAsync(temp, TreeCommand(), TimeSpan.FromSeconds(2));

        Assert.True(result.IsError);
        Assert.Contains("timed out after 2s; process tree killed", result.Output);
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
            ? "powershell.exe -NoProfile -Command '$PID' > grandchild.pid & sleep 300"
            : "sh -c 'echo $$ > grandchild.pid; exec sleep 300' & sleep 300";

    private static async Task<int> WaitForPidAsync(string pidFile)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (File.Exists(pidFile))
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

            await Task.Delay(100);
        }

        Assert.Fail($"no pid was written to {pidFile} within 5 seconds");
        return 0;
    }

    private static async Task AssertEventuallyDeadAsync(int pid)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (!IsAlive(pid))
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"pid {pid} was still alive 5 seconds after the tree kill");
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
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

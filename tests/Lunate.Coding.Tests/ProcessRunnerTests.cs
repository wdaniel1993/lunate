using System.Diagnostics;
using System.Globalization;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The core/process units: the POSIX descendant walk (repointed from PosixProcessTreeTests), exit
/// codes and streams, the environment policy, the output cap, the stdin line pump and the kill on
/// timeout and cancellation.
/// </summary>
public sealed class ProcessRunnerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Enumerate_orders_children_before_parents_and_root_last()
    {
        // 100 ← 200 ← {300, 400}; 500 is unrelated.
        const string table = """
            100 1
            200 100
            300 200
            400 200
            500 1
            """;

        IReadOnlyList<int> ordered = ProcessRunner.EnumerateDescendants(table, 100);

        Assert.Equal([300, 400, 200, 100], ordered);
    }

    [Fact]
    public void Enumerate_ignores_malformed_and_self_referencing_lines()
    {
        const string table = """
            garbage line here
            1 1
            abc 100
            100 notanumber
            700 100
            """;

        IReadOnlyList<int> ordered = ProcessRunner.EnumerateDescendants(table, 100);

        Assert.Equal([700, 100], ordered);
    }

    [Fact]
    public void Enumerate_survives_a_parent_cycle()
    {
        const string table = """
            100 200
            200 100
            """;

        IReadOnlyList<int> ordered = ProcessRunner.EnumerateDescendants(table, 100);

        Assert.Equal([200, 100], ordered);
    }

    [Fact]
    public void Enumerate_of_a_pid_absent_from_the_table_returns_just_the_root()
    {
        const string table = """
            100 1
            """;

        IReadOnlyList<int> ordered = ProcessRunner.EnumerateDescendants(table, 999);

        Assert.Equal([999], ordered);
    }

    [Fact]
    public void Enumerate_parses_culture_invariantly()
    {
        // Style check: the shared parse path must not depend on the machine culture
        // (the suite also runs under de-AT).
        const string table = "100 1\n200 100\n";

        IReadOnlyList<int> ordered = ProcessRunner.EnumerateDescendants(table, 100);

        Assert.Equal(
            string.Join(',', ordered),
            string.Join(',', ordered.Select(pid => pid.ToString("0", CultureInfo.InvariantCulture)))
        );
    }

    [Fact]
    public async Task Exit_codes_and_both_streams_round_trip()
    {
        using var temp = new TempDirectory();

        ProcessRunResult result = await RunAsync(
            temp,
            OperatingSystem.IsWindows()
                ? new ProcessRunRequest(
                    "cmd.exe",
                    // '>&2 echo err' (redirect-first) avoids the trailing space cmd.exe leaves in
                    // 'echo err 1>&2'.
                    ["/c", "echo out& >&2 echo err& exit /b 7"],
                    temp.Root
                )
                : new ProcessRunRequest(
                    "/bin/sh",
                    ["-c", "echo out; echo err 1>&2; exit 7"],
                    temp.Root
                )
        );

        Assert.Equal(7, result.ExitCode);
        Assert.False(result.TimedOut);
        // The runner passes the child's bytes through verbatim (cmd.exe writes CRLF); normalize at
        // the test seam per the scripting-contract rule.
        Assert.Equal("out\n", result.Stdout.ReplaceLineEndings("\n"));
        Assert.Equal("err\n", result.Stderr.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task The_environment_is_exactly_what_the_caller_passes()
    {
        using var temp = new TempDirectory();
        var environment = new Dictionary<string, string> { ["LUNATE_SERVICE_PROBE"] = "planted" };
        if (OperatingSystem.IsWindows())
        {
            // Windows cannot start a process without SystemRoot (DLL resolution); it is supplied
            // explicitly like any other caller-chosen variable — nothing else leaks in.
            environment["SystemRoot"] =
                Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        }

        var request = NodeRequest(
            temp,
            "console.log(process.env.LUNATE_SERVICE_PROBE ?? 'none');"
                + "console.log(process.env.PATH ?? 'none');"
        ) with
        {
            Environment = environment,
        };

        ProcessRunResult result = await RunAsync(temp, request);

        Assert.Equal(0, result.ExitCode);
        // The supplied variable arrives; the runner's own environment (PATH included) does not.
        Assert.Equal("planted\nnone\n", result.Stdout.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Output_over_the_cap_is_truncated_and_read_to_the_end()
    {
        using var temp = new TempDirectory();
        var request = NodeRequest(
            temp,
            "setTimeout(() => {}, 2000); process.stdout.write('x'.repeat(200000));"
        ) with
        {
            OutputCapBytes = 4096,
        };

        ProcessRunResult result = await RunAsync(temp, request);

        // The child wrote past the cap and still exited: the runner kept draining, so it never
        // blocked on a full pipe.
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.True(result.StdoutCapped);
        Assert.False(result.StderrCapped);
        Assert.Equal(4096, result.Stdout.Length);
        Assert.True(result.Stdout.All(character => character == 'x'));
    }

    [Fact]
    public async Task The_line_handler_answers_every_stdout_line_in_order()
    {
        using var temp = new TempDirectory();
        var seen = new List<string>();
        var request = NodeRequest(
            temp,
            "process.stdout.write('ping\\n');"
                + "process.stdin.once('data', chunk => {"
                + "process.stdout.write('got:' + chunk.toString().trim() + '\\n');"
                + "process.exit(0); });"
        ) with
        {
            LineHandler = (line, _) =>
            {
                seen.Add(line);
                return Task.FromResult<string?>(line == "ping" ? "pong" : null);
            },
        };

        ProcessRunResult result = await RunAsync(temp, request);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["ping", "got:pong"], seen);
        Assert.Contains("got:pong", result.Stdout);
    }

    [Fact]
    public async Task A_timeout_kills_the_process_tree()
    {
        using var temp = new TempDirectory();
        string pidFile = temp.File("grandchild.pid");

        // Windows needs a longer window: PowerShell's cold start on a loaded runner can exceed
        // the short Unix timeout, so the pid would never be written before the tree is killed.
        TimeSpan timeout = OperatingSystem.IsWindows()
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromSeconds(2);

        ProcessRunResult result = await RunAsync(temp, TreeRequest(temp, timeout));

        Assert.True(result.TimedOut);
        Assert.Null(result.ExitCode);
        int pid = await WaitForPidAsync(pidFile);
        await AssertEventuallyDeadAsync(pid);
    }

    [Fact]
    public async Task Cancellation_kills_the_tree_and_propagates()
    {
        using var temp = new TempDirectory();
        string pidFile = temp.File("grandchild.pid");
        using var cts = new CancellationTokenSource();
        var runner = new ProcessRunner();

        Task<ProcessRunResult> execution = runner.RunAsync(
            TreeRequest(temp, TimeSpan.FromSeconds(60)),
            cts.Token
        );
        int pid = await WaitForPidAsync(pidFile);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        await AssertEventuallyDeadAsync(pid);
    }

    private static async Task<ProcessRunResult> RunAsync(
        TempDirectory temp,
        ProcessRunRequest request
    )
    {
        var runner = new ProcessRunner();
        ProcessRunResult result = await runner.RunAsync(request, Ct);
        return result;
    }

    private static ProcessRunRequest NodeRequest(
        TempDirectory temp,
        string script,
        TimeSpan? timeout = null
    ) => new("node", ["-e", script], temp.Root, Timeout: timeout ?? TimeSpan.FromSeconds(60));

    private static ProcessRunRequest TreeRequest(TempDirectory temp, TimeSpan timeout) =>
        OperatingSystem.IsWindows()
            ? new ProcessRunRequest(
                "powershell.exe",
                [
                    "-NoProfile",
                    "-Command",
                    "Set-Content -Path grandchild.pid -Value $PID; Start-Sleep 300",
                ],
                temp.Root,
                Timeout: timeout
            )
            : new ProcessRunRequest(
                "/bin/sh",
                ["-c", "sh -c 'echo $$ > grandchild.pid; exec sleep 300' & sleep 300"],
                temp.Root,
                Timeout: timeout
            );

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

            await Task.Delay(100, Ct);
        }

        Assert.Fail($"no pid was written to {pidFile} within 60 seconds");
        return 0;
    }

    private static async Task AssertEventuallyDeadAsync(int pid)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if (!IsAlive(pid))
            {
                return;
            }

            await Task.Delay(100, Ct);
        }

        Assert.Fail($"pid {pid} was still alive 30 seconds after the tree kill");
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
}

using System.Diagnostics;
using System.Globalization;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// What <see cref="UnixChildProcess"/>'s spawn recipe guarantees: the guarded spawn works, a long
/// environment does not stop it, and the child is detached from the terminal — its own session, no
/// controlling tty, <c>/dev/null</c> stdin. These run the real OS spawn, so the guard test needs the
/// real <c>sandbox-exec</c>.
/// </summary>
public sealed class UnixChildProcessTests
{
    internal static readonly ResolvedShell Bash = new(ShellKind.Bash, "/bin/bash", "bash", ["-c"]);

    [Fact]
    public async Task A_plain_spawn_round_trips_output_and_exit_code()
    {
        using var temp = new TempDirectory();
        var request = new ProcessRunRequest("/bin/echo", ["spawn-ok"], temp.Root);
        using Lunate.Coding.ChildProcess child = UnixChildProcess.Spawn(request);
        string stdout = await child.StandardOutput.ReadToEndAsync(
            TestContext.Current.CancellationToken
        );
        int exit = await child.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Equal("spawn-ok\n", stdout);
    }

    [Fact]
    public async Task The_child_is_its_own_session_leader_with_no_controlling_tty()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        string pidFile = temp.File("child.pid");
        var request = new ProcessRunRequest(
            "/bin/bash",
            ["-c", $"echo $$ > '{pidFile}'; sleep 30"],
            temp.Root
        );
        using Lunate.Coding.ChildProcess child = UnixChildProcess.Spawn(request);

        string pidText = await PidFileAsync(pidFile);
        var (pid, pgid, _, tty) = IdentityOf(pidText);

        // setsid made the child the leader of a brand-new session: it leads its own process
        // group — not the group the test host lives in — and holds no controlling terminal,
        // so it cannot open /dev/tty behind Lunate's back.
        Assert.Equal(pid, pgid);
        Assert.NotEqual(HostProcessGroupId(), pgid);
        Assert.Equal("??", tty);

        await child.KillTreeAsync();
    }

    private static async Task<string> PidFileAsync(string pidFile)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (File.Exists(pidFile))
            {
                string text = File.ReadAllText(pidFile).Trim();
                if (int.TryParse(text, out _))
                {
                    return text;
                }
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"the child never wrote its pid to {pidFile}");
        return string.Empty;
    }

    private static (int Pid, int Pgid, int Ppid, string Tty) IdentityOf(string pidText)
    {
        var start = new ProcessStartInfo("/bin/ps")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "-o", "pid=,pgid=,ppid=,tty=", "-p", pidText },
        };
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        string[] fields = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4)
        {
            Assert.Fail($"ps could not describe pid {pidText}: {error}");
        }

        return (
            int.Parse(fields[0], CultureInfo.InvariantCulture),
            int.Parse(fields[1], CultureInfo.InvariantCulture),
            int.Parse(fields[2], CultureInfo.InvariantCulture),
            fields[3]
        );
    }

    private static int HostProcessGroupId() =>
        IdentityOf(Environment.ProcessId.ToString(CultureInfo.InvariantCulture)).Pgid;

    [Fact]
    public async Task The_child_reads_stdin_at_end_of_file()
    {
        using var temp = new TempDirectory();
        var request = new ProcessRunRequest(
            "/bin/bash",
            ["-c", "if read line; then echo read-ok; else echo read-eof; fi"],
            temp.Root
        );
        using Lunate.Coding.ChildProcess child = UnixChildProcess.Spawn(request);
        string stdout = await child.StandardOutput.ReadToEndAsync(
            TestContext.Current.CancellationToken
        );
        int exit = await child.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Equal("read-eof\n", stdout);
    }

    [Fact]
    public async Task A_half_megabyte_env_value_spawns()
    {
        using var temp = new TempDirectory();
        var request = new ProcessRunRequest("/bin/echo", ["big"], temp.Root)
        {
            Environment = new Dictionary<string, string> { ["BIG"] = new string('x', 512 * 1024) },
        };
        using Lunate.Coding.ChildProcess child = UnixChildProcess.Spawn(request);
        string stdout = await child.StandardOutput.ReadToEndAsync(
            TestContext.Current.CancellationToken
        );
        int exit = await child.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Equal("big\n", stdout);
    }

    [Fact]
    public async Task The_full_bash_guard_composition_runs_and_keeps_the_child_out_of_the_terminal()
    {
        if (!OperatingSystem.IsMacOS())
        {
            // The guard wraps with sandbox-exec only on macOS; other platforms have their own
            // mechanisms covered by the BashGuard resolution tests.
            return;
        }

        // The manual counterpart on a real terminal (the CI runner has neither a tty nor tmux):
        // inside tmux run lunate, start a tool call that touches the terminal
        // (bash -c 'sleep 30'), then press Esc and Ctrl+C: the turn stops, the termios `stty -a`
        // reads before == after, and typing immediately prints in the input line.
        string work = Directory.GetCurrentDirectory();
        var guard = new BashGuard();
        GuardCommand launch = guard.Wrap(Bash, work);
        Assert.True(launch.Wrapped, $"guard unwrapped: {launch.Notice ?? "no notice"}");

        var request = new ProcessRunRequest(
            launch.FileName,
            // stty instead of ps: the seatbelt refuses to exec ps inside the sandbox, and what is
            // needed here is terminal evidence anyway — a child whose stdin is /dev/null and that
            // has no controlling terminal cannot reach the session's raw mode.
            [
                .. launch.ArgumentsPrefix,
                "echo guarded-ok; if stty -a; then echo tty-ok; else echo no-tty; fi",
            ],
            work
        )
        {
            ExtraEnvironment = BashTool.SessionEnvironment,
        };
        using Lunate.Coding.ChildProcess child = UnixChildProcess.Spawn(request);
        string stdout = await child.StandardOutput.ReadToEndAsync(
            TestContext.Current.CancellationToken
        );
        await child.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        int exit = await child.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Contains("guarded-ok", stdout, StringComparison.Ordinal);
        // The child's termios question is answered from its own mouth: stdin is not a terminal.
        Assert.Contains("no-tty", stdout, StringComparison.Ordinal);
    }
}

using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The interactive <c>core/process</c> session: line round-trips, the exact environment, the
/// bounded line reader, stderr, exit observation and the tree kill.
/// </summary>
public sealed class ProcessSessionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lines_round_trip_through_an_interactive_child()
    {
        using var temp = new TempDirectory();
        await using IProcessSession session = await StartAsync(
            temp,
            EchoScript,
            maxLineBytes: null
        );

        await session.WriteInputLineAsync("first", Ct);
        Assert.Equal("echo:first", await session.ReadOutputLineAsync(Ct));
        await session.WriteInputLineAsync("second", Ct);
        Assert.Equal("echo:second", await session.ReadOutputLineAsync(Ct));

        Assert.Equal(0, await session.WaitForExitAsync(Ct));
        Assert.Null(await session.ReadOutputLineAsync(Ct));
    }

    [Fact]
    public async Task The_environment_is_exactly_what_the_caller_passes()
    {
        using var temp = new TempDirectory();
        var environment = EnvironmentFor("LUNATE_SESSION_PROBE", "planted");
        IProcessSession session = await StartAsync(
            temp,
            "process.stdout.write((process.env.LUNATE_SESSION_PROBE ?? 'none') + '\\n');"
                + "process.stdout.write((process.env.PATH ?? 'none') + '\\n');",
            maxLineBytes: null,
            environment
        );
        await using (session)
        {
            Assert.Equal("planted", await session.ReadOutputLineAsync(Ct));
            Assert.Equal("none", await session.ReadOutputLineAsync(Ct));
        }
    }

    [Fact]
    public async Task An_oversized_line_is_drained_rejected_and_the_session_continues()
    {
        using var temp = new TempDirectory();
        IProcessSession session = await StartAsync(
            temp,
            "process.stdout.write('x'.repeat(2000) + '\\n');"
                + "process.stdout.write('after' + '\\n');",
            maxLineBytes: 256
        );
        await using (session)
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await session.ReadOutputLineAsync(Ct)
            );
            Assert.Equal("after", await session.ReadOutputLineAsync(Ct));
        }
    }

    [Fact]
    public async Task Stderr_lines_are_readable_independently()
    {
        using var temp = new TempDirectory();
        IProcessSession session = await StartAsync(
            temp,
            "process.stdout.write('out\\n'); process.stderr.write('err\\n');",
            maxLineBytes: null
        );
        await using (session)
        {
            Assert.Equal("out", await session.ReadOutputLineAsync(Ct));
            Assert.Equal("err", await session.ReadErrorLineAsync(Ct));
        }
    }

    [Fact]
    public async Task A_child_that_exits_yields_its_code_and_closed_streams()
    {
        using var temp = new TempDirectory();
        await using IProcessSession session = await StartAsync(
            temp,
            "process.exit(7);",
            maxLineBytes: null
        );

        Assert.Equal(7, await session.WaitForExitAsync(Ct));
        Assert.True(session.HasExited);
        Assert.Equal(7, session.ExitCode);
        Assert.Null(await session.ReadOutputLineAsync(Ct));
    }

    [Fact]
    public async Task Killing_the_session_kills_the_process_tree()
    {
        using var temp = new TempDirectory();
        string pidFile = temp.File("grandchild.pid");
        DateTime commandStarted = DateTime.Now;
        IProcessSession session = await StartAsync(temp, TreeScript, maxLineBytes: null);

        ChildProcess grandchild = await ChildProcessProbe.AwaitWrittenAsync(
            pidFile,
            commandStarted,
            CancellationToken.None
        );
        await session.KillAsync();

        await ChildProcessProbe.AssertEventuallyDeadAsync(grandchild);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_a_running_session_kills_the_child()
    {
        using var temp = new TempDirectory();
        string pidFile = temp.File("grandchild.pid");
        DateTime commandStarted = DateTime.Now;
        IProcessSession session = await StartAsync(temp, TreeScript, maxLineBytes: null);

        ChildProcess grandchild = await ChildProcessProbe.AwaitWrittenAsync(
            pidFile,
            commandStarted,
            CancellationToken.None
        );
        await session.DisposeAsync();

        await ChildProcessProbe.AssertEventuallyDeadAsync(grandchild);
    }

    [Fact]
    public async Task Write_after_exit_is_a_teaching_error()
    {
        using var temp = new TempDirectory();
        await using IProcessSession session = await StartAsync(
            temp,
            "process.exit(0);",
            maxLineBytes: null
        );
        await session.WaitForExitAsync(Ct);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await session.WriteInputLineAsync("late", Ct)
        );
        Assert.Contains("stdin is closed", exception.Message, StringComparison.Ordinal);
    }

    private static string EchoScript =>
        "let buffer = '';"
        + "process.stdin.on('data', chunk => {"
        + "  buffer += chunk.toString();"
        + "  let index;"
        + "  while ((index = buffer.indexOf('\\n')) >= 0) {"
        + "    const line = buffer.slice(0, index);"
        + "    buffer = buffer.slice(index + 1);"
        + "    process.stdout.write('echo:' + line + '\\n');"
        + "    if (line === 'second') { process.exit(0); }"
        + "  }"
        + "});";

    // The grandchild is another node (its path is absolute, so the scrubbed environment needs no
    // PATH) sleeping long enough to outlive any accidental non-tree kill.
    private static string TreeScript =>
        "const { spawn } = require('child_process');"
        + "const fs = require('fs');"
        + "const child = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], "
        + "{ stdio: 'ignore' });"
        + "fs.writeFileSync('grandchild.pid', String(child.pid));"
        + "setInterval(() => {}, 1000);";

    private static async Task<IProcessSession> StartAsync(
        TempDirectory temp,
        string script,
        int? maxLineBytes,
        IReadOnlyDictionary<string, string>? environment = null
    ) =>
        await new ProcessRunner().StartSessionAsync(
            new ProcessSessionRequest(
                "node",
                ["-e", script],
                temp.Root,
                environment ?? EnvironmentFor(),
                maxLineBytes
            ),
            Ct
        );

    private static IReadOnlyDictionary<string, string> EnvironmentFor(
        string? name = null,
        string? value = null
    )
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (OperatingSystem.IsWindows())
        {
            // Windows cannot start a process without SystemRoot (DLL resolution).
            environment["SystemRoot"] =
                Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        }

        if (name is not null && value is not null)
        {
            environment[name] = value;
        }

        return environment;
    }
}

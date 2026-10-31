using Lunate.Tui;

namespace Lunate.Coding.Tests;

public sealed class BashGuardStatusSessionTests
{
    [Fact]
    public async Task A_disabled_guard_skips_the_startup_hardlink_scan()
    {
        using var scanRoot = new TempDirectory();
        string stateRoot = scanRoot.File("state");
        using var host = new InteractiveSessionHost(
            environment: EnvironmentWithKey(),
            hardlinkScanRoot: stateRoot,
            hardlinkScanFileSystem: new ExternalLinkFixture(stateRoot)
        );
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """{"schemaVersion":2,"bashGuard":false}"""
        );
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);
        string header = await host.WaitForScrollbackAsync(
            $"lunate  v{ProductVersion.Build}"
        );

        Assert.DoesNotContain("pre-existing hardlink", header, StringComparison.Ordinal);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_bash_tool_reuses_the_guard_resolved_for_the_header()
    {
        GuardProbe fixtureProbe = ProbeFor("none-failure");
        int probeCalls = 0;
        GuardProbe probe = fixtureProbe with
        {
            Run = (fileName, arguments) =>
            {
                probeCalls++;
                return fixtureProbe.Run(fileName, arguments);
            },
        };
        using var host = new InteractiveSessionHost(
            environment: EnvironmentWithKey(),
            bashGuardProbe: probe
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("!echo guard-status-reuse");
        await host.WaitUntilAsync(() => host.Session.IsBashMode);
        host.Console.SendEnter();
        await host.WaitForApprovalAsync();
        host.Console.SendText("y");
        string scrollback = await host.WaitForScrollbackAsync(
            text => text.Contains("exit 0", StringComparison.Ordinal) && !host.Session.IsBashRunning,
            "the guarded bash result"
        );

        Assert.Contains("guard-status-reuse", scrollback, StringComparison.Ordinal);
        Assert.Equal(2, probeCalls);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Theory]
    [InlineData("sandbox-success", "sandbox-exec", "success", true, 1)]
    [InlineData("sandbox-failure", "none", "failure", true, 1)]
    [InlineData("bwrap-success", "bwrap", "success", true, 1)]
    [InlineData("landlock-success", "landlock", "success", true, 1)]
    [InlineData("none-failure", "none", "failure", true, 2)]
    [InlineData("disabled", "none", "not run", false, 0)]
    public async Task Context_and_header_report_the_same_fixture_guard_status(
        string fixture,
        string mechanism,
        string probeState,
        bool enabled,
        int expectedProbeCalls
    )
    {
        GuardProbe fixtureProbe = ProbeFor(fixture);
        int probeCalls = 0;
        GuardProbe probe = fixtureProbe with
        {
            Run = (fileName, arguments) =>
            {
                probeCalls++;
                return fixtureProbe.Run(fileName, arguments);
            },
        };
        using var host = new InteractiveSessionHost(
            environment: EnvironmentWithKey(),
            bashGuardProbe: probe
        );
        host.Console.Size = new ConsoleSize(400, 24);
        File.WriteAllText(
            host.Temp.File("settings.json"),
            $$"""{"schemaVersion":2,"bashGuard":{{enabled.ToString().ToLowerInvariant()}}}"""
        );
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);
        string header = await host.WaitForScrollbackAsync(
            $"lunate  v{ProductVersion.Build}"
        );
        string expectedHeader =
            enabled
                ? $"bash guard {mechanism} · probe {probeState}"
                : "bash guard disabled · none · probe not run";
        Assert.Contains(expectedHeader, header, StringComparison.Ordinal);

        host.Console.SendText("/context");
        host.Console.SendEnter();
        string expectedContext = $"bash guard: {mechanism}; probe: {probeState}";
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "context: ");

        Assert.Contains(expectedContext, host.LastFrame, StringComparison.Ordinal);
        Assert.Equal(expectedProbeCalls, probeCalls);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static GuardProbe ProbeFor(string fixture)
    {
        GuardProbe probe = fixture switch
        {
            "sandbox-success" => Fake.Unix(macOs: true, sandboxExec: true),
            "sandbox-failure" => Fake.Unix(macOs: true, sandboxExec: true) with
            {
                Run = (_, _) => (1, "sandbox-exec fixture failure"),
            },
            "bwrap-success" => Fake.Unix(macOs: false, bwrap: "/usr/bin/bwrap"),
            "landlock-success" => Fake.Unix(macOs: false, setpriv: "/usr/bin/setpriv"),
            "none-failure" => Fake.Unix(
                macOs: false,
                bwrap: "/usr/bin/bwrap",
                setpriv: "/usr/bin/setpriv",
                bwrapOutput: "bwrap fixture failure",
                setprivOutput: "setpriv fixture failure"
            ),
            "disabled" => Fake.Unix(macOs: false, bwrap: "/usr/bin/bwrap"),
            _ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, "unknown fixture"),
        };

        return probe with { FileExists = _ => true };
    }

    private static Func<string, string?> EnvironmentWithKey()
    {
        Func<string, string?> baseline = PrintModeTestSupport.Environment();
        return name => name switch
        {
            "LUNATE_UNICODE" => "1",
            "OPENAI_API_KEY" => "test-key",
            _ => baseline(name),
        };
    }

    private sealed class ExternalLinkFixture(string stateRoot) : IHardlinkScanFileSystem
    {
        public bool DirectoryExists(string path) => path == stateRoot;

        public IReadOnlyList<string> EnumerateDirectories(string path) => [];

        public IReadOnlyList<string> EnumerateFiles(string path) =>
            path == stateRoot ? [Path.Combine(path, "linked.json")] : [];

        public bool IsDirectoryLink(string path) => false;

        public HardlinkFileMetadata? GetFileMetadata(string path) =>
            new(new HardlinkFileIdentity(1, 11), LinkCount: 2);
    }
}

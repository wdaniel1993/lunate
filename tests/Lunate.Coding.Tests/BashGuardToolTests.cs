using System.Globalization;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The guard as it behaves through the real <c>bash</c> tool, against a temporary state root:
/// the refusal on this machine's mechanism, one normal command's unchanged result and the
/// unsupported-Windows notice.
/// </summary>
public sealed class BashGuardToolTests
{
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());

    [Fact]
    public async Task On_macos_a_state_root_write_is_refused_and_names_the_guard()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using var fixture = new GuardFixture();
        var result = await RunAsync(fixture, $"echo pwn > {fixture.State}/trust.json");

        Assert.True(result.IsError);
        Assert.Contains("refused by the bash guard (macOS sandbox-exec)", result.Output);
        Assert.Contains(fixture.State, result.Output);
        Assert.False(File.Exists(Path.Combine(fixture.State, "trust.json")));
    }

    [Fact]
    public async Task On_linux_a_state_root_write_is_refused_and_is_never_left_on_disk()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var fixture = new GuardFixture();
        if (!fixture.GuardReady)
        {
            // A broken guard must fail this test, not pass it: the runner might lack a
            // mechanism, but then the resolution says so and the platform matrix is wrong.
            Assert.Fail(
                $"the guard did not resolve on this Linux runner; its report: {fixture.Notice}"
            );
        }

        var result = await RunAsync(fixture, $"echo pwn > {fixture.State}/trust.json");

        Assert.True(
            result.IsError,
            $"expected the guard to refuse the state-root write, got: {result.Output}"
        );
        Assert.Contains("refused by the bash guard", result.Output);
        Assert.Contains(fixture.State, result.Output);
        Assert.False(
            File.Exists(Path.Combine(fixture.State, "trust.json")),
            "the state root must not hold the file the refused command would have written"
        );
    }

    [Fact]
    public async Task A_normal_command_runs_with_its_unchanged_result()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return;
        }

        using var fixture = new GuardFixture();
        if (!fixture.GuardReady)
        {
            return; // No guard mechanism on this runner; the unavailable notice test covers it.
        }

        var result = await RunAsync(fixture, "echo hello");

        Assert.True(!result.IsError, $"expected a clean run, got: {result.Output}");
        Assert.Equal("hello\nexit code: 0", result.Output);
    }

    [Fact]
    public async Task A_disabled_guard_skips_the_probe_and_is_named_in_the_result()
    {
        using var temp = new TempDirectory();
        string workspace = Directory.CreateDirectory(temp.File("workspace")).FullName;
        string state = Directory.CreateDirectory(temp.File("state")).FullName;
        string prefix = Directory.CreateDirectory(temp.File("prefix")).FullName;
        var target = new GuardTarget(state, Path.Combine(state, "auth.json"), prefix, temp.Root);
        int probeCalls = 0;
        GuardProbe probe = GuardProbe.Current with
        {
            IsWindows = false,
            IsMacOs = false,
            IsLinux = true,
            FindOnPath = name => name == "bwrap" ? "bwrap" : null,
            FileExists = _ => true,
            DirectoryExists = _ => true,
            ListDirectories = _ => [],
            ListFiles = _ => [],
            Run = (_, _) =>
            {
                probeCalls++;
                return (0, string.Empty);
            },
        };
        var tool = new BashTool(
            new Workspace(workspace),
            null,
            null,
            null,
            new BashGuard(target, probe),
            guardEnabled: false
        );

        ToolResult result = await tool.ExecuteAsync(
            CommandArgs("echo guard-disabled"),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("bash guard is disabled", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, probeCalls);
    }

    [Fact]
    public async Task On_macos_the_auth_store_file_is_readable_neither_plain_nor_silently()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using var fixture = new GuardFixture();
        File.WriteAllText(Path.Combine(fixture.State, "auth.json"), "SECRET");

        var result = await RunAsync(fixture, $"cat {fixture.Auth}");

        Assert.True(result.IsError, $"expected a refusal, got: {result.Output}");
        Assert.Contains("bash guard", result.Output);
        Assert.DoesNotContain("SECRET", result.Output);
    }

    [Fact]
    public async Task On_windows_the_command_runs_and_says_it_is_unconfined()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var log = new RecordingExtensionLog();
        var tool = new BashTool(new Workspace(temp.Root), log: log);

        var first = await tool.ExecuteAsync(
            CommandArgs("echo first"),
            Context,
            CancellationToken.None
        );

        // The warning is recorded where a user can find it, and the command's own bytes are untouched:
        // a notice prefixed to stdout is a change to every script's output on this platform.
        Assert.DoesNotContain("unconfined", first.Output);
        Assert.Contains(log.Messages, m => m.Contains("unconfined", StringComparison.Ordinal));
        Assert.Contains(log.Messages, m => m.Contains("Windows", StringComparison.Ordinal));

        var second = await tool.ExecuteAsync(
            CommandArgs("echo second"),
            Context,
            CancellationToken.None
        );

        Assert.Equal(
            1,
            log.Messages.Count(m => m.Contains("unconfined", StringComparison.Ordinal))
        );
    }

    private static async Task<ToolResult> RunAsync(GuardFixture fixture, string command)
    {
        var tool = new BashTool(
            new Workspace(fixture.Workspace),
            null,
            null,
            null,
            new BashGuard(fixture.Target, GuardProbe.Current)
        );

        return await tool.ExecuteAsync(CommandArgs(command), Context, CancellationToken.None);
    }

    private static JsonElement CommandArgs(string command) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { command })).RootElement.Clone();

    private static string Formatted(string text) => text;

    private sealed class GuardFixture : IDisposable
    {
        public GuardFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "lunate-guard-tests",
                Guid.NewGuid().ToString("N")
            );
            Workspace = Directory.CreateDirectory(Path.Combine(Root, "workspace")).FullName;
            State = Directory.CreateDirectory(Path.Combine(Root, "state")).FullName;
            Prefix = Directory.CreateDirectory(Path.Combine(Root, "prefix")).FullName;
            Auth = Path.Combine(State, "auth.json");
            Target = new GuardTarget(State, Auth, Prefix, Root);

            var shell = new ShellResolver().Resolve();
            if (shell is { } resolved)
            {
                GuardCommand command = new BashGuard(Target, GuardProbe.Current).Wrap(
                    resolved,
                    Workspace
                );
                GuardReady = command.Wrapped;
                Notice = command.Notice ?? "(no report)";
            }
            else
            {
                GuardReady = false;
                Notice = "the shell did not resolve";
            }
        }

        public string Root { get; }
        public string Workspace { get; }
        public string State { get; }
        public string Prefix { get; }
        public string Auth { get; }
        public GuardTarget Target { get; }
        public bool GuardReady { get; }
        public string Notice { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException) { }
        }
    }
}

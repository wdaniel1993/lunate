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
    public async Task On_linux_a_state_root_write_is_refused_or_the_restriction_is_named()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var fixture = new GuardFixture();
        var result = await RunAsync(fixture, $"echo pwn > {fixture.State}/trust.json");

        Assert.False(string.IsNullOrWhiteSpace(result.Output));
        Assert.True(
            result.IsError || result.Output.Contains("runs unconfined", StringComparison.Ordinal),
            $"expected a refusal or the unavailability report, got: {result.Output}"
        );
        Assert.True(
            result.Output.Contains("refused by the bash guard", StringComparison.Ordinal)
                || result.Output.Contains("the bash guard is unavailable", StringComparison.Ordinal)
                || result.Output.Contains("does not support", StringComparison.Ordinal),
            $"the output does not name the guard or its state: {result.Output}"
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

        Assert.True(
            !result.IsError,
            $"expected a clean run, got: {result.Output}"
        );
        Assert.Equal("hello\nexit code: 0", result.Output);
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
        var tool = new BashTool(new Workspace(temp.Root));

        var first = await tool.ExecuteAsync(CommandArgs("echo first"), Context, CancellationToken.None);

        Assert.Contains("unconfined", first.Output);
        Assert.Contains("Windows", first.Output);

        var second = await tool.ExecuteAsync(CommandArgs("echo second"), Context, CancellationToken.None);

        Assert.DoesNotContain("unconfined", second.Output);
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
            GuardReady = shell is { } resolved
                && new BashGuard(Target, GuardProbe.Current).Wrap(resolved, Workspace).Wrapped;
        }

        public string Root { get; }
        public string Workspace { get; }
        public string State { get; }
        public string Prefix { get; }
        public string Auth { get; }
        public GuardTarget Target { get; }
        public bool GuardReady { get; }

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

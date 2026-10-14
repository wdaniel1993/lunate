using Lunate.Agent;
using Lunate.Coding;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Testing;

namespace WorktreeTasksExtension.Tests;

internal static class TestSupport
{
    /// <summary>The extension directory the test build copies under the output root.</summary>
    public static string ExtensionDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "WorktreeTasksExtension");

    /// <summary>
    /// Creates a host over the built sample extension. The harness builds the child tools for
    /// another workspace root from the production (public) coding tools, exactly as the shipping
    /// composition does through its workspace-tool factory.
    /// </summary>
    public static ExtensionTestHost CreateHost(TempDirectory temp) =>
        new(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "worktree",
                ExtensionDirectory = ExtensionDirectory,
                ConfigureHarness = options => options with { WorkspaceTools = ChildTools },
            }
        );

    public static IProcessRunner ProcessRunner(ExtensionTestHost host)
    {
        Assert.True(
            host.Runner.Services.TryGetCore<IProcessRunner>(
                "core/process",
                out IProcessRunner? runner
            ) && runner is not null,
            "the kit must expose the core/process service"
        );
        return runner;
    }

    /// <summary>Initializes a real git repository in the host's working directory with one commit.</summary>
    public static async Task InitRepositoryAsync(ExtensionTestHost host, string fileName)
    {
        IProcessRunner runner = ProcessRunner(host);
        await GitAsync(host, runner, "init", "-b", "main");
        await GitAsync(host, runner, "config", "user.email", "worktree-tests@example.com");
        await GitAsync(host, runner, "config", "user.name", "Worktree Tests");
        File.WriteAllText(Path.Combine(host.WorkingDirectory, fileName), "base\n");
        await GitAsync(host, runner, "add", ".");
        await GitAsync(host, runner, "commit", "-m", "initial commit");
    }

    public static async Task<(int ExitCode, string Output)> GitAsync(
        ExtensionTestHost host,
        IProcessRunner runner,
        params string[] arguments
    )
    {
        ProcessRunResult result = await runner.RunAsync(
            new ProcessRunRequest("git", arguments, host.WorkingDirectory),
            TestContext.Current.CancellationToken
        );
        return (result.ExitCode ?? -1, result.Stdout + result.Stderr);
    }

    /// <summary>The worktree path the extension derives for a name under the host's root.</summary>
    public static string WorktreePath(ExtensionTestHost host, string name)
    {
        string root = Path.GetFullPath(host.WorkingDirectory);
        return Path.Combine(Path.GetDirectoryName(root)!, Path.GetFileName(root) + "-" + name);
    }

    public static string ChildSessionPath(string parentSessionPath, string childSessionId) =>
        Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(parentSessionPath))!,
            SessionPaths.SessionFileName(childSessionId)
        );

    /// <summary>Reads the repo/worktree/cwd fields of a session file's header line.</summary>
    public static (string? Repo, string? Worktree, string? Cwd) ReadHeader(string sessionPath)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(
            File.ReadLines(sessionPath).First()
        );
        System.Text.Json.JsonElement root = document.RootElement;
        return (
            root.TryGetProperty("repo", out System.Text.Json.JsonElement repo)
                ? repo.GetString()
                : null,
            root.TryGetProperty("worktree", out System.Text.Json.JsonElement worktree)
                ? worktree.GetString()
                : null,
            root.TryGetProperty("cwd", out System.Text.Json.JsonElement cwd)
                ? cwd.GetString()
                : null
        );
    }

    private static ToolRegistry ChildTools(string root)
    {
        var workspace = new Workspace(root);
        var tools = new ToolRegistry();
        tools.Add(new ReadTool(workspace));
        tools.Add(new WriteTool(workspace));
        tools.Add(new EditTool(workspace));
        tools.Add(new BashTool(workspace));
        return tools;
    }
}

/// <summary>A no-op event sink for direct tool invocations outside a run.</summary>
internal sealed class NoopAgentEvents : IAgentEvents
{
    public void Emit(AgentEvent agentEvent) { }
}

/// <summary>A canned subagent runner for direct tool invocations outside a run.</summary>
internal sealed class StubSubagentRunner : ISubagentRunner
{
    public int Depth => 0;

    public Task<SubagentResult> RunAsync(SubagentRequest request, CancellationToken ct) =>
        Task.FromResult(
            new SubagentResult(null, "run_stub", "stub child", StopReasons.Stop, false, 0)
        );
}

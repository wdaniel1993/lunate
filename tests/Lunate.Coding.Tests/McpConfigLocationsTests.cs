using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Protocols;

namespace Lunate.Coding.Tests;

/// <summary>
/// The five standard mcp.json locations and their merge: precedence within a scope (.lunate >
/// .agents > root .mcp.json), user scope before project scope, one collision log line per shadowed
/// definition naming the server and both files, and the expansion warnings following the winning
/// definition.
/// </summary>
public sealed class McpConfigLocationsTests
{
    [Fact]
    public void Merge_replaces_by_name_and_logs_the_collision_naming_both_files()
    {
        List<McpServerOptions> servers = [];
        Dictionary<string, int> index = new(StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<string>> missing = new(StringComparer.Ordinal);
        List<string> log = [];

        McpComposition.MergeInto(
            servers,
            index,
            missing,
            [
                new McpServerOptions("a", "user-a", [], Source: "/user/.lunate/mcp.json"),
                new McpServerOptions("b", "user-b", [], Source: "/user/.lunate/mcp.json"),
            ],
            _ => [],
            log.Add
        );
        McpComposition.MergeInto(
            servers,
            index,
            missing,
            [
                new McpServerOptions("b", "project-b", [], Source: "/repo/.agents/mcp.json"),
                new McpServerOptions("c", "project-c", [], Source: "/repo/.agents/mcp.json"),
            ],
            _ => [],
            log.Add
        );

        Assert.Equal(["a", "b", "c"], servers.Select(server => server.Name));
        Assert.Equal("user-a", servers[0].Command);
        Assert.Equal("project-b", servers[1].Command);
        Assert.Equal("project-c", servers[2].Command);
        string collision = Assert.Single(log);
        Assert.Contains("server 'b'", collision, StringComparison.Ordinal);
        Assert.Contains("'/repo/.agents/mcp.json'", collision, StringComparison.Ordinal);
        Assert.Contains("'/user/.lunate/mcp.json'", collision, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_config_paths_are_ordered_by_precedence()
    {
        string root = Path.Combine("worktree");

        Assert.Equal(
            [
                McpComposition.ProjectConfigPath(root),
                McpComposition.ProjectAgentsConfigPath(root),
                McpComposition.ProjectRootConfigPath(root),
            ],
            McpComposition.ProjectConfigPaths(root)
        );
        Assert.EndsWith(
            Path.Combine(".agents", "mcp.json"),
            McpComposition.ProjectAgentsConfigPath(root),
            StringComparison.Ordinal
        );
        Assert.EndsWith(
            $".mcp.json",
            McpComposition.ProjectRootConfigPath(root),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task The_user_agents_file_wins_over_the_lunate_user_file_and_logs_the_collision()
    {
        using var temp = new TempDirectory();
        string worktree = Directory.CreateDirectory(temp.File("worktree")).FullName;
        string lunate = temp.File("user-lunate-mcp.json");
        File.WriteAllText(lunate, """{ "mcpServers": { "srv": { "command": "from-lunate" } } }""");
        string agents = temp.File("user-agents-mcp.json");
        File.WriteAllText(agents, """{ "mcpServers": { "srv": { "command": "from-agents" } } }""");
        var log = new RecordingExtensionLog();

        McpToolSet? set = await Compose(worktree, lunate, agents, log);

        McpServerStatus status = Assert.Single(set!.Statuses);
        Assert.Equal("srv", status.Name);
        Assert.Equal(agents, status.Source);
        Assert.True(
            log.Contains($"server 'srv' from '{agents}' replaces the same name from '{lunate}'"),
            "the collision must be logged; log:\n" + string.Join("\n", log.Messages)
        );
    }

    [Fact]
    public async Task An_unset_variable_warning_is_a_notice_and_stays_on_the_status()
    {
        const string Variable = "LUNATE_TEST_UNSET_24f8";
        Environment.SetEnvironmentVariable(Variable, null);
        using var temp = new TempDirectory();
        string worktree = Directory.CreateDirectory(temp.File("worktree")).FullName;
        string config = temp.File("user-lunate-mcp.json");
        File.WriteAllText(
            config,
            $$"""{ "mcpServers": { "srv": { "command": "run", "env": { "TOKEN": "${LUNATE_TEST_UNSET_24f8}" } } } }"""
        );
        List<UserNotice> notices = [];

        McpToolSet set = (
            await Compose(
                worktree,
                config,
                temp.File("user-agents-mcp.json"),
                new RecordingExtensionLog(),
                notices
            )
        )!;

        Assert.Equal(["LUNATE_TEST_UNSET_24f8"], Assert.Single(set.Statuses).MissingVariables);
        UserNotice notice = Assert.Single(notices);
        Assert.Equal(NoticeLevel.Warning, notice.Level);
        Assert.Contains("'srv'", notice.Message, StringComparison.Ordinal);
        Assert.Contains("'LUNATE_TEST_UNSET_24f8'", notice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_shadowed_servers_warning_is_dropped_with_its_definition()
    {
        const string Variable = "LUNATE_TEST_UNSET_5c1d";
        Environment.SetEnvironmentVariable(Variable, null);
        using var temp = new TempDirectory();
        string worktree = Directory.CreateDirectory(temp.File("worktree")).FullName;
        string lunate = temp.File("user-lunate-mcp.json");
        File.WriteAllText(
            lunate,
            $$"""{ "mcpServers": { "srv": { "command": "${LUNATE_TEST_UNSET_5c1d}" } } }"""
        );
        string agents = temp.File("user-agents-mcp.json");
        File.WriteAllText(agents, """{ "mcpServers": { "srv": { "command": "fine" } } }""");
        List<UserNotice> notices = [];

        McpToolSet set = (
            await Compose(worktree, lunate, agents, new RecordingExtensionLog(), notices)
        )!;

        Assert.Empty(Assert.Single(set.Statuses).MissingVariables!);
        Assert.DoesNotContain(
            notices,
            notice => notice.Message.Contains("LUNATE_TEST_UNSET_5c1d", StringComparison.Ordinal)
        );
    }

    private static async Task<McpToolSet?> Compose(
        string worktree,
        string lunatePath,
        string agentsPath,
        RecordingExtensionLog log,
        List<UserNotice>? notices = null
    ) =>
        await McpComposition.ComposeAsync(
            new Workspace(worktree),
            lunatePath,
            agentsPath,
            new ExtensionSeams { StorePath = Path.Combine(worktree, "store"), Log = log },
            new RefusingPrompt(),
            new RefusingApprovalPrompt(),
            log,
            notice => notices?.Add(notice),
            TestContext.Current.CancellationToken
        );

    private sealed class RefusingPrompt : IMcpConfigTrustPrompt
    {
        public ValueTask<bool> ApproveAsync(
            string configPath,
            IReadOnlyList<McpServerOptions> servers,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(false);
    }

    private sealed class RefusingApprovalPrompt : IMcpServerApprovalPrompt
    {
        public ValueTask<bool> ApproveAsync(
            McpServerOptions server,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(false);
    }
}

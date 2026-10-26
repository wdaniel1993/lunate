using System.Text.Json;
using Lunate.Agent;
using Lunate.Protocols;
using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

[Collection("mcp-processes")]
public sealed class McpToolSetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Servers_start_once_and_record_their_status()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        await using var set = new McpToolSet(
            [
                new McpServerOptions(
                    "test",
                    TestMcpServer.DotnetPath,
                    ["exec", TestMcpServer.ServerDll],
                    new Dictionary<string, string> { ["LUNATE_TEST_MCP_MARKER"] = marker },
                    NamespaceName: "test",
                    Source: "test-source"
                ),
            ],
            log: static _ => { }
        );

        McpServerStatus before = Assert.Single(set.Statuses);
        Assert.Equal(McpServerState.NotStarted, before.State);
        Assert.False(File.Exists(marker), "construction must start nothing");

        IReadOnlyList<ITool> tools = await set.ListToolsAsync(Ct);

        Assert.Equal(5, tools.Count);
        Assert.All(tools, tool => Assert.StartsWith("test__", tool.Name, StringComparison.Ordinal));
        McpServerStatus after = Assert.Single(set.Statuses);
        Assert.Equal(McpServerState.Started, after.State);
        Assert.Equal(5, after.ToolCount);
        Assert.Null(after.Reason);
        Assert.Equal("test-source", after.Source);

        int pid = TestMcpServer.StartedPid(marker);
        await set.ListToolsAsync(Ct);
        Assert.Single(
            File.ReadAllLines(marker),
            line => line.StartsWith("started pid=", StringComparison.Ordinal)
        );

        await set.DisposeAsync();
        await TestMcpServer.WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Only_workspace_independent_servers_tools_are_offered_to_other_roots()
    {
        using var temp = new TempDirectory();
        await using var set = new McpToolSet(
            [
                new McpServerOptions(
                    "project",
                    TestMcpServer.DotnetPath,
                    ["exec", TestMcpServer.ServerDll],
                    NamespaceName: "project",
                    Source: "mcp.json"
                ),
                new McpServerOptions(
                    "global",
                    TestMcpServer.DotnetPath,
                    ["exec", TestMcpServer.ServerDll],
                    NamespaceName: "global",
                    Source: "mcp.json",
                    WorkspaceIndependent: true
                ),
            ],
            log: static _ => { }
        );

        IReadOnlyList<ITool> tools = await set.ListToolsAsync(Ct);

        Assert.Equal(10, tools.Count);
        IReadOnlyList<ITool> independent = set.ListedToolsFrom(options =>
            options.WorkspaceIndependent
        );
        Assert.Equal(5, independent.Count);
        Assert.All(
            independent,
            tool => Assert.StartsWith("global__", tool.Name, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_crashed_server_flips_to_failed_once_and_notifies_once()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        List<string> log = [];
        List<McpServerStatus> notified = [];
        await using var set = new McpToolSet(
            [
                new McpServerOptions(
                    "test",
                    TestMcpServer.DotnetPath,
                    ["exec", TestMcpServer.ServerDll],
                    new Dictionary<string, string> { ["LUNATE_TEST_MCP_MARKER"] = marker },
                    NamespaceName: "test",
                    Source: "test-source"
                ),
            ],
            log: log.Add
        );
        set.OnServerExited(status => notified.Add(status));

        Assert.Equal(5, (await set.ListToolsAsync(Ct)).Count);
        int pid = TestMcpServer.StartedPid(marker);
        await TestMcpServer.KillAsync(pid);

        IReadOnlyList<McpServerStatus> observed = await set.ObserveStatusesAsync(Ct);

        McpServerStatus status = Assert.Single(observed);
        Assert.Equal(McpServerState.Failed, status.State);
        Assert.Equal(McpToolSet.ServerExitedReason, status.Reason);
        Assert.Equal("test-source", status.Source);
        McpServerStatus callback = Assert.Single(notified);
        Assert.Equal("test-source", callback.Source);
        Assert.Single(
            log,
            message => message.Contains(McpToolSet.ServerExitedReason, StringComparison.Ordinal)
        );

        IReadOnlyList<McpServerStatus> again = await set.ObserveStatusesAsync(Ct);

        Assert.Equal(McpServerState.Failed, Assert.Single(again).State);
        Assert.Single(notified);
        Assert.Single(
            log,
            message => message.Contains(McpToolSet.ServerExitedReason, StringComparison.Ordinal)
        );
        Assert.Empty(await set.ListToolsAsync(Ct));
    }

    [Fact]
    public async Task A_slow_server_times_out_the_probe_but_stays_started_without_notices()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        List<string> log = [];
        List<McpServerStatus> notified = [];
        await using var set = new McpToolSet(
            [
                new McpServerOptions(
                    "test",
                    TestMcpServer.DotnetPath,
                    ["exec", TestMcpServer.ServerDll],
                    new Dictionary<string, string>
                    {
                        ["LUNATE_TEST_MCP_MARKER"] = marker,
                        ["LUNATE_TEST_MCP_SLOW_LIST_MS"] = "2500",
                    },
                    NamespaceName: "test",
                    Source: "test-source"
                ),
            ],
            log: log.Add
        );
        set.OnServerExited(status => notified.Add(status));

        Assert.Equal(5, (await set.ListToolsAsync(Ct)).Count);

        IReadOnlyList<McpServerStatus> observed = await set.ObserveStatusesAsync(Ct);

        McpServerStatus status = Assert.Single(observed);
        Assert.Equal(McpServerState.Started, status.State);
        Assert.Equal(5, status.ToolCount);
        Assert.Null(status.Reason);
        Assert.Empty(notified);
        Assert.Empty(log);
    }

    [Fact]
    public async Task A_failing_server_is_isolated_recorded_and_the_others_still_work()
    {
        using var temp = new TempDirectory();
        await using var set = new McpToolSet(
            [
                new McpServerOptions(
                    "broken",
                    "lunate-mcp-command-that-cannot-exist",
                    [],
                    NamespaceName: "broken"
                ),
                new McpServerOptions(
                    "test",
                    TestMcpServer.DotnetPath,
                    ["exec", TestMcpServer.ServerDll],
                    new Dictionary<string, string>
                    {
                        ["LUNATE_TEST_MCP_MARKER"] = temp.File("marker.txt"),
                    },
                    NamespaceName: "test"
                ),
            ],
            log: static _ => { }
        );

        IReadOnlyList<ITool> tools = await set.ListToolsAsync(Ct);

        Assert.Equal(5, tools.Count);
        Assert.All(tools, tool => Assert.StartsWith("test__", tool.Name, StringComparison.Ordinal));
        List<McpServerStatus> statuses = [.. set.Statuses];
        Assert.Equal(McpServerState.Failed, statuses[0].State);
        Assert.Equal("broken", statuses[0].Name);
        Assert.Contains("broken", statuses[0].Reason, StringComparison.Ordinal);
        Assert.Equal(McpServerState.Started, statuses[1].State);
        Assert.Equal(5, statuses[1].ToolCount);
    }

    [Fact]
    public async Task A_tool_list_change_reaches_the_callback_and_the_sets_next_listing()
    {
        using var temp = new TempDirectory();
        await using var set = new McpToolSet(
            [
                new McpServerOptions(
                    "test",
                    TestMcpServer.DotnetPath,
                    ["exec", TestMcpServer.ServerDll],
                    new Dictionary<string, string>
                    {
                        ["LUNATE_TEST_MCP_MARKER"] = temp.File("marker.txt"),
                    },
                    NamespaceName: "test"
                ),
            ],
            log: static _ => { }
        );
        var changed = new TaskCompletionSource<(string Server, IReadOnlyList<ITool> Tools)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        set.OnToolsChanged(
            (server, tools) =>
            {
                if (tools.Any(tool => tool.Name == "test__late_tool"))
                {
                    changed.TrySetResult((server, tools));
                }
            }
        );

        IReadOnlyList<ITool> tools = await set.ListToolsAsync(Ct);
        ITool spawn = tools.Single(tool => tool.Name == "test__spawn_tool");
        ToolResult result = await spawn.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            new ToolContext("run", new NullAgentEvents()),
            Ct
        );
        Assert.False(result.IsError);

        // The server's tools/list_changed notification reaches this set's consumer, named by server.
        (string server, IReadOnlyList<ITool> updated) = await changed.Task.WaitAsync(
            TimeSpan.FromSeconds(30),
            Ct
        );
        Assert.Equal("test", server);
        Assert.Contains(updated, tool => tool.Name == "test__late_tool");

        // And the set's own view moved: a later listing and the workspace-independent lookup both
        // carry the new tool without another ListToolsAsync call by the consumer.
        IReadOnlyList<ITool> relisted = await set.ListToolsAsync(Ct);
        Assert.Contains(relisted, tool => tool.Name == "test__late_tool");
        Assert.Contains(
            set.ListedToolsFrom(options => options.Name == "test"),
            tool => tool.Name == "test__late_tool"
        );
    }
}

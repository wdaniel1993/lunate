using Lunate.Agent;
using Lunate.Protocols;

namespace Lunate.Coding.Tests;

public sealed class McpToolSetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Servers_start_once_and_record_their_status()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        await using var set = new McpToolSet([
            new McpServerOptions(
                "test",
                McpTestServerSupport.DotnetPath,
                ["exec", McpTestServerSupport.ServerDll],
                new Dictionary<string, string> { ["LUNATE_TEST_MCP_MARKER"] = marker },
                NamespaceName: "test",
                Source: "test-source"
            ),
        ]);

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

        int pid = McpTestServerSupport.StartedPid(marker);
        await set.ListToolsAsync(Ct);
        Assert.Single(
            File.ReadAllLines(marker),
            line => line.StartsWith("started pid=", StringComparison.Ordinal)
        );

        await set.DisposeAsync();
        await McpTestServerSupport.WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(10));
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
                    McpTestServerSupport.DotnetPath,
                    ["exec", McpTestServerSupport.ServerDll],
                    new Dictionary<string, string> { ["LUNATE_TEST_MCP_MARKER"] = marker },
                    NamespaceName: "test",
                    Source: "test-source"
                ),
            ],
            log: log.Add
        );
        set.OnServerExited(status => notified.Add(status));

        Assert.Equal(5, (await set.ListToolsAsync(Ct)).Count);
        int pid = McpTestServerSupport.StartedPid(marker);
        await McpTestServerSupport.KillAsync(pid);

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
    public async Task A_failing_server_is_isolated_recorded_and_the_others_still_work()
    {
        using var temp = new TempDirectory();
        await using var set = new McpToolSet([
            new McpServerOptions(
                "broken",
                "lunate-mcp-command-that-cannot-exist",
                [],
                NamespaceName: "broken"
            ),
            new McpServerOptions(
                "test",
                McpTestServerSupport.DotnetPath,
                ["exec", McpTestServerSupport.ServerDll],
                new Dictionary<string, string>
                {
                    ["LUNATE_TEST_MCP_MARKER"] = temp.File("marker.txt"),
                },
                NamespaceName: "test"
            ),
        ]);

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
}

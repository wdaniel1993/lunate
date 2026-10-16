using Lunate.TestSupport;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The T-30 done-when, interactively: a real stdio server from a temp mcp.json is listed as
/// <c>server__tool</c>, its call round-trips through approval and renders, <c>/mcp</c> shows the
/// live status, and a broken server notifies without harming the session.
/// </summary>
[Collection("mcp-processes")]
public sealed class McpInteractiveEndToEndTests
{
    /// <summary>
    /// The frame-wait window for frames that depend on a child process starting: the first spawn
    /// in a run is cold (dotnet exec on a loaded CI runner), and 10 seconds is not enough there —
    /// the repo's spawn-window idiom (BashToolTests uses 30s for the same race).
    /// </summary>
    private static readonly TimeSpan SpawnWait = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_configured_server_round_trips_renders_and_mcp_lists_its_status()
    {
        using var temp = new TempDirectory();
        string config = temp.File("mcp.json");
        using var host = new InteractiveSessionHost(mcpConfigPath: config);
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test"));
        host.Client.Enqueue(
            Scripts.Call("call-1", "test__echo", Scripts.Args(("text", "hello mcp"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null, SpawnWait);
        Assert.Equal("test__echo", host.Session.PendingApproval!.ToolName);
        host.Console.SendText("y");
        await host.Client.WaitForCallAsync(2);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "hello mcp", SpawnWait);

        Assert.Contains("test__echo", host.Client.Requests[0][0].Text!, StringComparison.Ordinal);
        Assert.Contains("tool test__echo", host.ScrollbackText, StringComparison.Ordinal);

        host.Console.SendText("/mcp");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "test — started, 5 tools",
            SpawnWait
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_broken_server_notifies_and_mcp_shows_both_statuses()
    {
        using var temp = new TempDirectory();
        string config = temp.File("mcp.json");
        File.WriteAllText(
            config,
            $$"""
            {
              "mcpServers": {
                "broken": { "command": "lunate-mcp-command-that-cannot-exist" },
                "test": {{McpTestServerSupport.EntryJson(temp.File("marker.txt"))}}
              }
            }
            """
        );
        using var host = new InteractiveSessionHost(mcpConfigPath: config);
        host.Console.Size = new ConsoleSize(400, 24);
        Task run = host.RunAsync();

        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "MCP server 'broken'",
            SpawnWait
        );
        Assert.Contains("The session continues", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("/mcp");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "broken — failed:", SpawnWait);
        Assert.Contains("test — started, 5 tools", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Mcp_shows_the_config_path_as_the_source()
    {
        using var temp = new TempDirectory();
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test"));
        using var host = new InteractiveSessionHost(mcpConfigPath: config);
        host.Console.Size = new ConsoleSize(400, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/mcp");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "test — started", SpawnWait);
        Assert.Contains($"({config})", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Mcp_shows_the_agents_config_path_as_the_source()
    {
        using var temp = new TempDirectory();
        string config = temp.File("agents-mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test"));
        using var host = new InteractiveSessionHost(mcpAgentsConfigPath: config);
        host.Console.Size = new ConsoleSize(400, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/mcp");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "test — started", SpawnWait);
        Assert.Contains($"({config})", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Mcp_shows_a_missing_variable_warning_next_to_the_server()
    {
        const string Variable = "LUNATE_TEST_UNSET_9b41";
        Environment.SetEnvironmentVariable(Variable, null);
        using var temp = new TempDirectory();
        string config = temp.File("mcp.json");
        File.WriteAllText(
            config,
            $$"""
            { "mcpServers": { "test": { "command": "{{TestMcpServer.DotnetPath}}", "args": ["exec", "{{TestMcpServer.ServerDll}}"], "env": { "LUNATE_TEST_MCP_MARKER": "{{temp.File(
                "marker.txt"
            )}}", "TOKEN": "${LUNATE_TEST_UNSET_9b41}" } } } }
            """
        );
        using var host = new InteractiveSessionHost(mcpConfigPath: config);
        host.Console.Size = new ConsoleSize(400, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/mcp");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "test — started", SpawnWait);
        Assert.Contains(
            "[missing ${LUNATE_TEST_UNSET_9b41}]",
            host.LastFrame,
            StringComparison.Ordinal
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Mcp_without_configured_servers_says_so()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("/mcp");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "mcp: no servers configured");

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}

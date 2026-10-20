using Lunate.Extensibility;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// Composition order in a real session: extension tools are declared before MCP tools (design D7).
/// </summary>
[Collection("mcp-processes")]
public sealed class ExtensionToolCompositionTests
{
    /// <summary>
    /// The frame-wait window for frames that depend on a child process starting: the first spawn
    /// in a run is cold (dotnet exec on a loaded CI runner), and 10 seconds is not enough there —
    /// the repo's spawn-window idiom (BashToolTests uses 30s for the same race).
    /// </summary>
    private static readonly TimeSpan SpawnWait = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Extension_tools_are_declared_before_mcp_tools()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        ToolFixture.Install(store);
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test"));
        using var host = new InteractiveSessionHost(
            mcpConfigPath: config,
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() }
        );
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        string system = host
            .Client.Requests[0]
            .First(message => message.Role == ChatRole.System)
            .Text!;
        int extension = system.IndexOf("tools__echo", StringComparison.Ordinal);
        int mcp = system.IndexOf("test__echo", StringComparison.Ordinal);
        Assert.True(extension >= 0, "the extension tool is not declared:\n" + system);
        Assert.True(mcp >= 0, "the MCP tool is not declared:\n" + system);
        Assert.True(extension < mcp, "extension tools must come before MCP tools:\n" + system);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    /// <summary>
    /// The reachable collision (design D1) through the real session: a direct extension tool
    /// (<c>test__echo</c>) and a configured server's same-named tool compose into exactly one
    /// declaration — the extension's — and executing it runs the extension handler, never the
    /// server's. The skip diagnostic arrives from the spawn-dependent listing, so its frame wait
    /// uses the 30s cold-runner window.
    /// </summary>
    [Fact]
    public async Task An_extension_tool_beats_a_colliding_mcp_tool_through_session_composition()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        ToolFixture.Install(store, id: "test");
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test"));
        using var host = new InteractiveSessionHost(
            mcpConfigPath: config,
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() }
        );
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "toolDetails": "expanded"
            }
            """
        );
        host.Console.Size = new ConsoleSize(400, 24);
        host.Client.Enqueue(
            Scripts.Call("call-1", "test__echo", Scripts.Args(("text", "hello mcp"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "first composition wins",
            SpawnWait
        );
        Assert.Contains("mcp.json", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains("(test)", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        // Exactly one declaration wins and it is the extension's: the system prompt lists the name
        // once and the request's declarations carry the extension's description, not the MCP
        // tool's.
        string system = host
            .Client.Requests[0]
            .First(message => message.Role == ChatRole.System)
            .Text!;
        int declared = system.IndexOf("test__echo", StringComparison.Ordinal);
        Assert.True(declared >= 0, "the winning tool is not declared:\n" + system);
        Assert.Equal(-1, system.IndexOf("test__echo", declared + 1, StringComparison.Ordinal));
        AIFunction winner = Assert.Single(
            host.Client.RequestOptions[0]!.Tools!.OfType<AIFunction>(),
            tool => tool.Name == "test__echo"
        );
        Assert.Equal("Tool 'echo'.", winner.Description);
        Assert.DoesNotContain(
            "Echoes the input text back.",
            host.Client.RequestOptions[0]!
                .Tools!.OfType<AIFunction>()
                .Select(tool => tool.Description)
        );

        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null, SpawnWait);
        Assert.Equal("test__echo", host.Session.PendingApproval!.ToolName);
        host.Console.SendText("y");
        await host.Client.WaitForCallAsync(2);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "echo: args=", SpawnWait);
        Assert.Contains("\u2713 test__echo", host.ScrollbackText, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}

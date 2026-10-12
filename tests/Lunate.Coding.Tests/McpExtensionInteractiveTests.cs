using System.Text.Json;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The interactive done-when for extension-registered MCP servers: `/mcp` lists them with status
/// and source, a configured server wins a collision and the registration is shown shadowed, no
/// environment value reaches the notice, and a crashed server reads failed.
/// </summary>
public sealed class McpExtensionInteractiveTests
{
    [Fact]
    public async Task Mcp_lists_an_extension_registered_server_with_status_and_source()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        string marker = temp.File("marker.txt");
        McpExtensionFixture.Install(store, serverName: "fake", markerPath: marker);
        var log = new RecordingExtensionLog();
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = log }
        );
        host.Console.Size = new ConsoleSize(400, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/mcp");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "fake — started, 5 tools (extension 'mcp')"
        );

        Assert.DoesNotContain("no servers configured", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Mcp_shows_a_collision_with_the_configured_server_winning()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        string extensionMarker = temp.File("extension-marker.txt");
        string configMarker = temp.File("config-marker.txt");
        McpExtensionFixture.Install(store, serverName: "test", markerPath: extensionMarker);
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test", configMarker));
        var log = new RecordingExtensionLog();
        using var host = new InteractiveSessionHost(
            mcpConfigPath: config,
            extensions: new ExtensionSeams { StorePath = store, Log = log }
        );
        host.Console.Size = new ConsoleSize(400, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        Assert.True(File.Exists(configMarker), "the configured server must start");
        Assert.False(File.Exists(extensionMarker), "the shadowed registration must never start");
        Assert.True(
            log.Contains("wins the name collision"),
            "the collision must be logged; log:\n" + string.Join("\n", log.Messages)
        );
        Assert.True(log.Contains(config), "the log must name the configured source");

        host.Console.SendText("/mcp");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "shadowed by");

        Assert.Contains(
            $"test — started, 5 tools ({config})",
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.Contains($"test — shadowed by {config}", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Mcp_never_prints_environment_values()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        string extensionMarker = temp.File("extension-sentinel-value-1337.txt");
        McpExtensionFixture.Install(store, serverName: "fake", markerPath: extensionMarker);
        string config = temp.File("mcp.json");
        File.WriteAllText(
            config,
            JsonSerializer.Serialize(
                new
                {
                    mcpServers = new Dictionary<string, object>
                    {
                        ["test"] = new
                        {
                            command = McpTestServerSupport.DotnetPath,
                            args = new[] { "exec", McpTestServerSupport.ServerDll },
                            env = new Dictionary<string, string>
                            {
                                ["LUNATE_SENTINEL_ENV"] = "configured-sentinel-value",
                            },
                        },
                    },
                }
            )
        );
        using var host = new InteractiveSessionHost(
            mcpConfigPath: config,
            extensions: new ExtensionSeams { StorePath = store }
        );
        host.Console.Size = new ConsoleSize(400, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/mcp");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "extension 'mcp'");

        Assert.Contains("test — started", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "configured-sentinel-value",
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "extension-sentinel-value-1337",
            host.LastFrame,
            StringComparison.Ordinal
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Mcp_shows_a_crashed_server_as_failed()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test", marker));
        using var host = new InteractiveSessionHost(mcpConfigPath: config);
        host.Console.Size = new ConsoleSize(400, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await McpTestServerSupport.KillAsync(McpTestServerSupport.StartedPid(marker));

        host.Console.SendText("/mcp");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "test — failed: the server process exited"
        );

        host.Console.SendText("/mcp");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "test — failed: the server process exited"
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_failed_call_to_a_crashed_server_fires_the_crash_notice()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test", marker));
        using var host = new InteractiveSessionHost(mcpConfigPath: config);
        host.Console.Size = new ConsoleSize(400, 24);
        host.Client.Enqueue(
            Scripts.Call("call-1", "test__echo", Scripts.Args(("text", "gone"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await McpTestServerSupport.KillAsync(McpTestServerSupport.StartedPid(marker));

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        host.Console.SendText("y");
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "test__echo");
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "MCP server 'test' failed: the server process exited"
        );

        host.Console.SendText("/mcp");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "test — failed: the server process exited"
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}

namespace Lunate.Coding.Tests;

/// <summary>
/// The configured-wins rule in the non-interactive modes: a registration sharing a configured
/// server's name is skipped before listing (the configured server starts, the registration never
/// does), the collision is logged naming both sources, and the run continues.
/// </summary>
[Collection("mcp-processes")]
public sealed class McpCollisionCompositionTests
{
    [Fact]
    public async Task A_print_run_skips_a_registration_shadowed_by_a_configured_server()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        string extensionMarker = temp.File("extension-marker.txt");
        string configMarker = temp.File("config-marker.txt");
        McpExtensionFixture.Install(store, serverName: "test", markerPath: extensionMarker);
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test", configMarker));
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            McpConfigPath = config,
            Extensions = new ExtensionSeams { StorePath = store, Log = log },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Empty(errors);
        Assert.True(File.Exists(configMarker), "the configured server must start");
        Assert.False(File.Exists(extensionMarker), "the shadowed registration must never start");
        Assert.True(
            log.Contains("was skipped"),
            "the collision must be logged; log:\n" + string.Join("\n", log.Messages)
        );
        Assert.True(log.Contains("wins the name collision"));
        Assert.True(log.Contains("'mcp'"), "the log must name the extension");
        Assert.True(log.Contains(config), "the log must name the configured source");
    }

    [Fact]
    public void An_acp_serve_skips_a_registration_shadowed_by_a_configured_server()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        string extensionMarker = temp.File("extension-marker.txt");
        string configMarker = temp.File("config-marker.txt");
        McpExtensionFixture.Install(store, serverName: "test", markerPath: extensionMarker);
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test", configMarker));
        var log = new RecordingExtensionLog();
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        using var errors = new StringWriter();

        int exitCode = AcpMode.Run(
            errors,
            input,
            output,
            TestContext.Current.CancellationToken,
            new ExtensionSeams { StorePath = store, Log = log },
            mcpConfigPath: config,
            mcpAgentsConfigPath: temp.File("agents-mcp.json")
        );

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(configMarker), "the configured server must start");
        Assert.False(File.Exists(extensionMarker), "the shadowed registration must never start");
        Assert.True(log.Contains("was skipped"));
        Assert.True(log.Contains("wins the name collision"));
    }

    [Fact]
    public async Task A_print_run_without_a_collision_starts_the_registration()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        string extensionMarker = temp.File("extension-marker.txt");
        McpExtensionFixture.Install(store, serverName: "fake", markerPath: extensionMarker);
        string config = temp.File("mcp.json");
        File.WriteAllText(
            config,
            McpTestServerSupport.ConfigJson("test", temp.File("config-marker.txt"))
        );
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            McpConfigPath = config,
            Extensions = new ExtensionSeams { StorePath = store, Log = log },
        };

        (int exitCode, _, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(extensionMarker), "a non-colliding registration must start");
        Assert.False(log.Contains("was skipped"));
    }
}

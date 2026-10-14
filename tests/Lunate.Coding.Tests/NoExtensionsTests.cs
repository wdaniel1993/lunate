namespace Lunate.Coding.Tests;

/// <summary>
/// The <c>--no-extensions</c> discipline: the extension store is never read in any mode, while a
/// configured mcp.json server still works (configured servers are user-curated, not third-party
/// bundles).
/// </summary>
[Collection("mcp-processes")]
public sealed class NoExtensionsTests
{
    [Fact]
    public void Interactive_mode_with_the_flag_cannot_resolve_extension_models()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        ModelFixture.Install(store);
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion":1,"model":"ext/model-fixture/auto"}"""
        );
        var console = new ScriptedConsoleIO { IsInteractive = true };
        console.Complete();
        using var diagnostics = new StringWriter();
        var options = new InteractiveSessionOptions
        {
            Factory = new FakeChatClientFactory(new ScriptedChatClient()),
            SettingsPath = temp.File("settings.json"),
            ModelsPath = temp.File("models.json"),
            AuthPath = temp.File("auth.json"),
            SessionDirectory = temp.File("sessions"),
            WorkingDirectory = temp.Root,
            HistoryPath = temp.File("history"),
            Environment = _ => null,
            Console = console,
            Scheduler = new ManualScheduler(),
            Scrollback = new Spectre.Console.Testing.TestConsole(),
            Diagnostics = diagnostics,
        };

        int exitCode = Cli.Run(
            ["--no-extensions"],
            new StringWriter(),
            new StringWriter(),
            TestContext.Current.CancellationToken,
            interactiveOptions: options,
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() }
        );

        Assert.Equal(0, exitCode);
        // Without the host there is no registry: the extension-only id fails as a catalog lookup.
        Assert.Contains(
            "'ext/model-fixture/auto' is not in the catalog",
            diagnostics.ToString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Print_mode_with_the_flag_skips_the_store_and_keeps_configured_servers()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        string extensionMarker = temp.File("load-marker.txt");
        HelloFixture.WriteSettings(store, "hello", extensionMarker);
        string mcpMarker = temp.File("mcp-marker.txt");
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test", mcpMarker));
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions printOptions = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Extensions = null,
            McpConfigPath = config,
        };
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["--no-extensions", "-p", "Hi"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            printOptions,
            extensions: new ExtensionSeams { StorePath = store, Log = log }
        );

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output.ToString());
        Assert.False(
            File.Exists(extensionMarker),
            "--no-extensions must not read the extension store"
        );
        Assert.True(File.Exists(mcpMarker), "the configured mcp.json server still works");
    }

    [Fact]
    public void Interactive_mode_with_the_flag_skips_the_store()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        string extensionMarker = temp.File("load-marker.txt");
        HelloFixture.WriteSettings(store, "hello", extensionMarker);
        string mcpMarker = temp.File("mcp-marker.txt");
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test", mcpMarker));
        var console = new ScriptedConsoleIO { IsInteractive = true };
        console.Complete();
        var options = new InteractiveSessionOptions
        {
            Factory = new FakeChatClientFactory(new ScriptedChatClient()),
            SettingsPath = temp.File("settings.json"),
            ModelsPath = temp.File("models.json"),
            AuthPath = temp.File("auth.json"),
            SessionDirectory = temp.File("sessions"),
            WorkingDirectory = temp.Root,
            HistoryPath = temp.File("history"),
            Environment = PrintModeTestSupport.Environment(),
            Console = console,
            Scheduler = new ManualScheduler(),
            Scrollback = new Spectre.Console.Testing.TestConsole(),
            McpConfigPath = config,
        };
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["--no-extensions"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            interactiveOptions: options,
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() }
        );

        Assert.Equal(0, exitCode);
        Assert.False(
            File.Exists(extensionMarker),
            "--no-extensions must not read the extension store"
        );
        Assert.True(File.Exists(mcpMarker), "the configured mcp.json server still works");
    }

    [Fact]
    public void Acp_with_the_flag_skips_the_store_and_keeps_configured_servers()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        string extensionMarker = temp.File("load-marker.txt");
        HelloFixture.WriteSettings(store, "hello", extensionMarker);
        string mcpMarker = temp.File("mcp-marker.txt");
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test", mcpMarker));
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        using var errors = new StringWriter();

        int exitCode = AcpMode.Run(
            errors,
            input,
            output,
            TestContext.Current.CancellationToken,
            new ExtensionSeams
            {
                StorePath = store,
                Log = new RecordingExtensionLog(),
                NoExtensions = true,
            },
            mcpConfigPath: config
        );

        Assert.Equal(0, exitCode);
        Assert.Empty(output.ToArray());
        Assert.False(
            File.Exists(extensionMarker),
            "--no-extensions must not read the extension store"
        );
        Assert.True(File.Exists(mcpMarker), "the configured mcp.json server still works");
    }

    [Fact]
    public void The_flag_is_accepted_by_version_and_by_the_acp_check()
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();

        Assert.Equal(
            0,
            Cli.Run(
                ["--no-extensions", "--version"],
                output,
                errors,
                TestContext.Current.CancellationToken
            )
        );
        Assert.NotEmpty(output.ToString());

        Assert.Equal(
            2,
            Cli.Run(
                ["--no-extensions", "--acp", "extra"],
                output,
                errors,
                TestContext.Current.CancellationToken
            )
        );
        Assert.Contains("Usage: lunate --acp", errors.ToString(), StringComparison.Ordinal);
    }
}

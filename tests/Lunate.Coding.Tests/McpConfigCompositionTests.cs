using System.Text.Json;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The configured-server composition in print mode: startup poison, broken-server isolation,
/// malformed entries, and the project-file trust gate with the global file still working.
/// </summary>
public sealed class McpConfigCompositionTests
{
    [Theory]
    [InlineData("--version")]
    [InlineData("--help")]
    [InlineData("--discover")]
    public void Non_session_commands_never_start_a_configured_server(string command)
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test", marker));
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            [command],
            output,
            errors,
            TestContext.Current.CancellationToken,
            printOptions: new PrintModeOptions { McpConfigPath = config }
        );

        Assert.False(
            File.Exists(marker),
            $"{command} must not read mcp.json, but the configured server started"
        );
        Assert.NotEqual(1, exitCode);
    }

    [Fact]
    public async Task A_print_session_with_the_same_config_does_start_the_server()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test", marker));
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            McpConfigPath = config,
        };

        (int exitCode, string output, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.True(
            File.Exists(marker),
            "a print session with the poisoned config must start the server"
        );
    }

    [Fact]
    public async Task A_broken_server_is_a_warning_naming_it_and_the_others_still_work()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        string config = temp.File("mcp.json");
        File.WriteAllText(
            config,
            $$"""
            {
              "mcpServers": {
                "broken": { "command": "lunate-mcp-command-that-cannot-exist" },
                "test": {{McpTestServerSupport.EntryJson(marker)}}
              }
            }
            """
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "test__echo", Scripts.Args(("text", "still works"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "echo",
            Json = true,
            Yolo = true,
            McpConfigPath = config,
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Contains("lunate: MCP server 'broken' failed", errors, StringComparison.Ordinal);
        Assert.Contains("The session continues", errors, StringComparison.Ordinal);
        JsonElement result = Assert.Single(
            PrintModeTestSupport.ParseLines(output),
            line => PrintModeTestSupport.TypeOf(line) == "tool_call_result"
        );
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Equal("still works", result.GetProperty("output").GetString());
    }

    [Fact]
    public async Task A_malformed_entry_is_a_warning_naming_the_server_and_field()
    {
        using var temp = new TempDirectory();
        string config = temp.File("mcp.json");
        File.WriteAllText(
            config,
            """
            { "mcpServers": { "noCommand": { "args": ["-y"] } } }
            """
        );
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("done"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            McpConfigPath = config,
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("done", output);
        Assert.Contains("'noCommand'", errors, StringComparison.Ordinal);
        Assert.Contains("'command'", errors, StringComparison.Ordinal);
        Assert.Contains(config, errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_untrusted_project_config_is_refused_and_global_servers_still_work()
    {
        using var temp = new TempDirectory();
        string globalMarker = temp.File("global-marker.txt");
        string projectMarker = temp.File("project-marker.txt");
        string globalConfig = temp.File("global-mcp.json");
        File.WriteAllText(
            globalConfig,
            $$"""{ "mcpServers": { "global-srv": {{McpTestServerSupport.EntryJson(globalMarker)}} } }"""
        );
        string projectConfig = McpComposition.ProjectConfigPath(temp.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(projectConfig)!);
        File.WriteAllText(
            projectConfig,
            $$"""{ "mcpServers": { "project-srv": {{McpTestServerSupport.EntryJson(projectMarker)}} } }"""
        );
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "global-srv__echo", Scripts.Args(("text", "from global"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "echo",
            Json = true,
            Yolo = true,
            McpConfigPath = globalConfig,
            Extensions = new ExtensionSeams { StorePath = temp.File("store"), Log = log },
        };

        (int exitCode, string output, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(globalMarker), "the global server must start");
        Assert.False(File.Exists(projectMarker), "an untrusted project server must not start");
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("was not trusted", StringComparison.Ordinal)
                && message.Contains("interactive", StringComparison.Ordinal)
        );
        JsonElement result = Assert.Single(
            PrintModeTestSupport.ParseLines(output),
            line => PrintModeTestSupport.TypeOf(line) == "tool_call_result"
        );
        Assert.Equal("from global", result.GetProperty("output").GetString());
    }

    [Fact]
    public async Task A_trusted_project_entry_overrides_a_same_named_global_entry()
    {
        using var temp = new TempDirectory();
        string globalConfig = temp.File("global-mcp.json");
        File.WriteAllText(
            globalConfig,
            """
            { "mcpServers": { "srv": { "command": "lunate-mcp-command-that-cannot-exist" } } }
            """
        );
        string projectConfig = McpComposition.ProjectConfigPath(temp.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(projectConfig)!);
        File.WriteAllText(projectConfig, McpTestServerSupport.ConfigJson("srv"));
        string storePath = temp.File("store");
        TrustProjectFile(storePath, temp.Root, projectConfig);
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "srv__echo", Scripts.Args(("text", "from project"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "echo",
            Json = true,
            Yolo = true,
            McpConfigPath = globalConfig,
            Extensions = new ExtensionSeams
            {
                StorePath = storePath,
                Log = new RecordingExtensionLog(),
            },
        };

        (int exitCode, string output, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        JsonElement result = Assert.Single(
            PrintModeTestSupport.ParseLines(output),
            line => PrintModeTestSupport.TypeOf(line) == "tool_call_result"
        );
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Equal("from project", result.GetProperty("output").GetString());
    }

    [Fact]
    public void Acp_composes_configured_servers_at_serve_start()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test", marker));
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        using var errors = new StringWriter();

        int exitCode = AcpMode.Run(
            errors,
            input,
            output,
            TestContext.Current.CancellationToken,
            TestExtensionSeams.Empty(temp),
            mcpConfigPath: config
        );

        Assert.Equal(0, exitCode);
        Assert.Empty(output.ToArray());
        Assert.True(
            File.Exists(marker),
            "the configured server must start at ACP serve start; errors: " + errors
        );
    }

    [Fact]
    public async Task A_malformed_project_file_reports_errors_only_when_trusted()
    {
        using var temp = new TempDirectory();
        string globalConfig = temp.File("global-mcp.json");
        File.WriteAllText(globalConfig, """{ "mcpServers": {} }""");
        string projectConfig = McpComposition.ProjectConfigPath(temp.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(projectConfig)!);
        File.WriteAllText(projectConfig, """{ "mcpServers": { """);
        string storePath = temp.File("store");
        string malformed = Path.Combine(".lunate", "mcp.json") + "' is not valid JSON";

        var untrustedLog = new RecordingExtensionLog();
        var untrustedClient = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions untrusted = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(untrustedClient)
        ) with
        {
            Prompt = "Hi",
            Json = true,
            McpConfigPath = globalConfig,
            Extensions = new ExtensionSeams { StorePath = storePath, Log = untrustedLog },
        };

        (int untrustedExit, _, string untrustedErrors) = await PrintModeTestSupport.RunAsync(
            untrusted
        );

        Assert.Equal(0, untrustedExit);
        Assert.Contains(
            untrustedLog.Messages,
            message => message.Contains("was not trusted", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(malformed, untrustedErrors, StringComparison.Ordinal);

        TrustProjectFile(storePath, temp.Root, projectConfig);
        var trustedClient = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions trusted = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(trustedClient)
        ) with
        {
            Prompt = "Hi",
            Json = true,
            McpConfigPath = globalConfig,
            Extensions = new ExtensionSeams
            {
                StorePath = storePath,
                Log = new RecordingExtensionLog(),
            },
        };

        (int trustedExit, _, string trustedErrors) = await PrintModeTestSupport.RunAsync(trusted);

        Assert.Equal(0, trustedExit);
        Assert.Contains(malformed, trustedErrors, StringComparison.Ordinal);
    }

    private static void TrustProjectFile(string storePath, string worktreeRoot, string configPath)
    {
        var store = new ExtensionTrustStore(new ExtensionHostOptions { StorePath = storePath });
        string canonical = Workspace.Canonicalize(worktreeRoot);
        store.RecordMcpConfigTrusted(
            canonical,
            canonical,
            ExtensionTrustStore.ComputeFileContentHash(configPath),
            DateTimeOffset.UtcNow
        );
    }
}

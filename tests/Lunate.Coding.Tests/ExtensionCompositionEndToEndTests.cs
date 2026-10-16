using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The done-when proof: an extension installed in a session's store genuinely runs through the
/// composed host in print, interactive and trust flows.
/// </summary>
public sealed class ExtensionCompositionEndToEndTests
{
    [Fact]
    public void A_print_run_observes_its_tool_call_through_an_installed_extension()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        File.WriteAllText(temp.File("a.txt"), "contents");
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "read", Scripts.Args(("path", "a.txt"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        PrintModeOptions printOptions = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        );
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["-p", "--json", "read a.txt"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            printOptions,
            extensions: new ExtensionSeams { StorePath = store, Log = log }
        );

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "\"type\":\"tool_call_result\"",
            output.ToString(),
            StringComparison.Ordinal
        );
        Assert.Empty(errors.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.True(
            log.Contains("tool call observed: read"),
            "the extension hook did not observe the run's tool call; log:\n"
                + string.Join("\n", log.Messages)
        );
        Assert.True(log.Contains("hello session started"));
        Assert.True(log.Contains("hello session ended"));
    }

    [Fact]
    public async Task An_interactive_session_draws_the_extensions_renderer_and_widget()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        var log = new RecordingExtensionLog();
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = log }
        );
        host.Console.Size = new ConsoleSize(200, 24);
        host.Client.Enqueue(
            Scripts.Call("call-1", "hello-tool", Scripts.Args()),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.Frames.Contains("hello-widget", StringComparison.Ordinal);
        });
        Assert.Contains("hello-widget", host.Frames, StringComparison.Ordinal);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "hello-rendered");

        Assert.Contains("hello-rendered", host.ScrollbackText, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public void An_acp_serve_composes_the_host_before_serving_and_stops_it_after()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
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
            mcpConfigPath: temp.File("global-mcp.json"),
            mcpAgentsConfigPath: temp.File("agents-mcp.json")
        );

        Assert.Equal(0, exitCode);
        Assert.True(
            log.Contains("hello session started"),
            "the ACP host must compose before serving; log:\n" + string.Join("\n", log.Messages)
        );
        Assert.True(log.Contains("hello session ended"));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task A_print_run_refuses_an_untrusted_project_extension_and_keeps_running()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(worktree, ".lunate", "extensions", "hello"));
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            WorkingDirectory = worktree,
            Extensions = new ExtensionSeams { StorePath = store, Log = log },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Empty(errors);
        Assert.True(
            log.Contains(
                "project extension 'hello' was not trusted; run an interactive session or 'lunate trust' to approve it."
            ),
            "the non-interactive run must log the interactive hint; log:\n"
                + string.Join("\n", log.Messages)
        );
        Assert.False(log.Contains("created hello"));
        Assert.False(File.Exists(Path.Combine(store, "trust.json")));
    }

    [Fact]
    public async Task A_print_run_loads_an_approved_project_extension_and_never_asks_again()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(worktree, ".lunate", "extensions", "hello"));
        var firstLog = new RecordingExtensionLog();
        var approval = new ScriptedTrustPrompt(true);
        var first = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(first)
        ) with
        {
            Prompt = "Hi",
            WorkingDirectory = worktree,
            Extensions = new ExtensionSeams
            {
                StorePath = store,
                Prompt = approval,
                Log = firstLog,
            },
        };

        (int firstExit, _, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, firstExit);
        Assert.Equal(1, approval.Calls);
        Assert.True(firstLog.Contains("created hello"));
        Assert.True(File.Exists(Path.Combine(store, "trust.json")));

        var secondLog = new RecordingExtensionLog();
        var noPrompt = new ScriptedTrustPrompt();
        var second = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions secondRun = options with
        {
            Factory = new FakeChatClientFactory(second),
            SessionDirectory = temp.File("sessions-2"),
            Extensions = new ExtensionSeams
            {
                StorePath = store,
                Prompt = noPrompt,
                Log = secondLog,
            },
        };

        (int secondExit, _, _) = await PrintModeTestSupport.RunAsync(secondRun);

        Assert.Equal(0, secondExit);
        Assert.Equal(0, noPrompt.Calls);
        Assert.True(secondLog.Contains("created hello"));
    }
}

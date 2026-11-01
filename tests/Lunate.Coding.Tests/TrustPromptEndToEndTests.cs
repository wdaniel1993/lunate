using Lunate.Extensibility;
using Lunate.TestSupport;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The trust prompt end to end: the question renders in the live area while the session composes,
/// keys answer it through the normal key path (type-ahead never can), several pending questions are
/// asked one at a time, and <c>/trust</c> lists and clears the records.
/// </summary>
[Collection("mcp-processes")]
public sealed class TrustPromptEndToEndTests
{
    [Fact]
    public async Task An_untrusted_project_extension_is_asked_in_the_live_area_and_y_loads_it()
    {
        using var first = new TrustSession();
        await first.QuestionAsync();

        Assert.Contains(
            "Load project extension 'hello' 0.1.0 from",
            first.Host.Frames,
            StringComparison.Ordinal
        );

        first.Host.Console.SendText("y");
        await first.Host.WaitUntilAsync(() => first.Log.Contains("created hello"));
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            first.Host,
            "remembered for this repository"
        );
        await first.QuitAsync();

        using var second = new TrustSession(worktree: first.Worktree, store: first.Store);
        await second.Host.WaitUntilAsync(() => second.Host.Session.IsComposed);

        Assert.True(second.Log.Contains("created hello"));
        Assert.DoesNotContain(
            "Load project extension",
            second.Host.Frames,
            StringComparison.Ordinal
        );
        await second.QuitAsync();
    }

    [Fact]
    public async Task Double_CtrlC_quits_while_the_trust_question_waits()
    {
        using var session = new TrustSession();
        await session.QuestionAsync();

        session.Host.Console.SendCtrlC();
        session.Host.Console.SendCtrlC();
        await session.Run;

        Assert.False(session.Log.Contains("created hello"));
    }

    [Fact]
    public async Task The_footer_shows_the_loading_status_while_composing()
    {
        using var session = new TrustSession();
        await session.QuestionAsync();

        Assert.Contains("loading project…", session.Host.Frames, StringComparison.Ordinal);

        session.Host.Console.SendText("n");
        await session.Host.WaitUntilAsync(() => session.Host.Session.IsComposed);
        session.Host.Advance(33);

        Assert.DoesNotContain("loading project…", session.Host.LastFrame, StringComparison.Ordinal);
        await session.QuitAsync();
    }

    [Fact]
    public async Task N_refuses_the_extension_and_logs_the_denial()
    {
        using var session = new TrustSession();
        await session.QuestionAsync();

        session.Host.Console.SendText("n");
        await session.Host.WaitUntilAsync(() => session.Host.Session.IsComposed);
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            session.Host,
            "nothing was recorded"
        );

        Assert.False(session.Log.Contains("created hello"));
        Assert.True(session.Log.Contains("was not trusted"));
        await session.QuitAsync();
    }

    [Fact]
    public async Task Enter_denies_the_extension()
    {
        using var session = new TrustSession();
        await session.QuestionAsync();

        session.Host.Console.SendEnter();
        await session.Host.WaitUntilAsync(() => session.Host.Session.IsComposed);

        Assert.False(session.Log.Contains("created hello"));
        await session.QuitAsync();
    }

    [Fact]
    public async Task Escape_denies_the_extension()
    {
        using var session = new TrustSession();
        await session.QuestionAsync();

        session.Host.Console.SendEscape();
        await session.Host.WaitUntilAsync(() => session.Host.Session.IsComposed);

        Assert.False(session.Log.Contains("created hello"));
        await session.QuitAsync();
    }

    [Fact]
    public async Task Capital_Y_approves_the_extension()
    {
        using var session = new TrustSession();
        await session.QuestionAsync();

        session.Host.Console.SendText("Y");
        await session.Host.WaitUntilAsync(() => session.Log.Contains("created hello"));
        await session.QuitAsync();
    }

    [Fact]
    public async Task CtrlC_does_not_decide_the_question()
    {
        using var session = new TrustSession();
        await session.QuestionAsync();

        session.Host.Console.SendCtrlC();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.NotNull(session.Host.Session.PendingTrustPrompt);
        Assert.False(session.Log.Contains("created hello"));

        session.Host.Console.SendText("y");
        await session.Host.WaitUntilAsync(() => session.Log.Contains("created hello"));
        await session.QuitAsync();
    }

    [Fact]
    public async Task Other_keys_leave_the_question_pending_and_the_input_inert()
    {
        using var session = new TrustSession();
        await session.QuestionAsync();

        session.Host.Console.SendText("abc");
        session.Host.Console.Send(new KeyEvent(KeyKind.Tab, null, false, false, false));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.NotNull(session.Host.Session.PendingTrustPrompt);
        session.Host.Advance(33);
        Assert.DoesNotContain("> abc", session.Host.LastFrame, StringComparison.Ordinal);

        session.Host.Console.SendText("y");
        await session.Host.WaitUntilAsync(() => session.Log.Contains("created hello"));
        await session.QuitAsync();
    }

    [Fact]
    public async Task Type_ahead_never_answers_the_question()
    {
        using var session = new TrustSession(typeAhead: "yes");
        await session.QuestionAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.NotNull(session.Host.Session.PendingTrustPrompt);
        Assert.False(session.Log.Contains("created hello"));

        session.Host.Console.SendText("y");
        await session.Host.WaitUntilAsync(() => session.Log.Contains("created hello"));
        await session.QuitAsync();
    }

    [Fact]
    public async Task A_key_stamped_before_the_arm_never_decides()
    {
        using var session = new TrustSession();
        await session.QuestionAsync();

        long arm = session.Host.Session.TrustArmSequence;
        await session.Host.Session.HandleKeyAsync(
            new KeyEvent(KeyKind.Character, "y", false, false, false),
            arm,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(session.Host.Session.PendingTrustPrompt);
        Assert.False(session.Log.Contains("created hello"));

        await session.Host.Session.HandleKeyAsync(
            new KeyEvent(KeyKind.Character, "y", false, false, false),
            arm + 1,
            TestContext.Current.CancellationToken
        );

        await session.Host.WaitUntilAsync(() => session.Log.Contains("created hello"));
        await session.QuitAsync();
    }

    [Fact]
    public async Task Several_pending_questions_are_asked_one_at_a_time()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        HelloFixture.Install(Path.Combine(worktree, ".lunate", "extensions", "hello"));
        string alphaMarker = temp.File("alpha.txt");
        string betaMarker = temp.File("beta.txt");
        File.WriteAllText(
            Path.Combine(worktree, ".lunate", "mcp.json"),
            $$"""
            { "mcpServers": {
                "alpha": {{McpTestServerSupport.EntryJson(alphaMarker)}},
                "beta": {{McpTestServerSupport.EntryJson(betaMarker)}} } }
            """
        );
        var log = new RecordingExtensionLog();
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = temp.File("store"), Log = log },
            workingDirectory: worktree
        );
        host.Console.Size = new ConsoleSize(400, 24);
        Task run = host.RunAsync();

        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "1 of 4");
        Assert.Contains("Load project extension 'hello'", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("y");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "2 of 4");
        Assert.Contains(
            "Start 2 servers from project mcp.json",
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.Contains("alpha — ", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains("beta — ", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("y");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "3 of 4");
        Assert.Contains(
            "Run project MCP server 'alpha', which sees your shell environment, from",
            host.LastFrame,
            StringComparison.Ordinal
        );

        host.Console.SendText("y");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "4 of 4");
        Assert.Contains(
            "Run project MCP server 'beta', which sees your shell environment, from",
            host.LastFrame,
            StringComparison.Ordinal
        );

        host.Console.SendText("y");
        await host.WaitUntilAsync(() => File.Exists(alphaMarker) && File.Exists(betaMarker));
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_denied_file_shrinks_the_remaining_question_count()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        worktree = Workspace.Canonicalize(worktree);
        string rootMarker = temp.File("root-marker.txt");
        string projectMarker = temp.File("project-marker.txt");
        string rootConfig = Path.Combine(worktree, ".mcp.json");
        File.WriteAllText(
            rootConfig,
            $$"""{ "mcpServers": { "root-srv": {{McpTestServerSupport.EntryJson(rootMarker)}} } }"""
        );
        string projectConfig = Path.Combine(worktree, ".lunate", "mcp.json");
        Directory.CreateDirectory(Path.GetDirectoryName(projectConfig)!);
        File.WriteAllText(
            projectConfig,
            $$"""{ "mcpServers": { "project-srv": {{McpTestServerSupport.EntryJson(projectMarker)}} } }"""
        );
        string storePath = temp.File("store");
        var store = new ExtensionTrustStore(new ExtensionHostOptions { StorePath = storePath });
        store.RecordMcpConfigTrusted(
            worktree,
            projectConfig,
            McpConfigTrustHash.ComputeFile(projectConfig),
            DateTimeOffset.UtcNow
        );
        store.RecordMcpConfigTrusted(
            worktree,
            rootConfig,
            "a-previous-hash",
            DateTimeOffset.UtcNow
        );
        var log = new RecordingExtensionLog();
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = storePath, Log = log },
            workingDirectory: worktree
        );
        host.Console.Size = new ConsoleSize(400, 24);
        Task run = host.RunAsync();

        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "1 of 3");
        Assert.Contains(
            "Start 1 server from project mcp.json",
            host.LastFrame,
            StringComparison.Ordinal
        );

        host.Console.SendText("n");
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "Run project MCP server 'project-srv'"
        );
        Assert.Contains("2 of 2", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("2 of 3", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("y");
        await host.WaitUntilAsync(() => File.Exists(projectMarker));
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;

        var reader = new ExtensionTrustStore(new ExtensionHostOptions { StorePath = storePath });
        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            reader.EvaluateMcpConfig(
                worktree,
                rootConfig,
                McpConfigTrustHash.ComputeFile(rootConfig)
            )
        );
        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            reader.EvaluateMcpConfig(worktree, rootConfig, "a-previous-hash")
        );
        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            reader.EvaluateMcpServer(
                worktree,
                McpConfigTrustHash.ComputeDefinitions(rootConfig)["root-srv"]
            )
        );
        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            reader.EvaluateMcpServer(
                worktree,
                McpConfigTrustHash.ComputeDefinitions(projectConfig)["project-srv"]
            )
        );
    }

    [Fact]
    public async Task A_per_server_approval_shows_the_full_command_and_y_approves_it()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        worktree = Workspace.Canonicalize(worktree);
        string marker = temp.File("server-marker.txt");
        string config = Path.Combine(worktree, ".lunate", "mcp.json");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("srv", marker));
        string store = temp.File("store");
        var log = new RecordingExtensionLog();
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = log },
            workingDirectory: worktree
        );
        host.Console.Size = new ConsoleSize(400, 24);
        Task run = host.RunAsync();

        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "Start 1 server from project mcp.json"
        );
        host.Console.SendText("y");
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "Run project MCP server 'srv'"
        );
        Assert.Contains("from '" + config + "'", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains(
            $"{TestMcpServer.DotnetPath} exec {TestMcpServer.ServerDll}",
            host.LastFrame,
            StringComparison.Ordinal
        );

        host.Console.SendText("y");
        await host.WaitUntilAsync(() => File.Exists(marker));
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;

        using var second = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            workingDirectory: worktree
        );
        Task secondRun = second.RunAsync();
        await second.WaitUntilAsync(() => second.Session.IsComposed);

        Assert.DoesNotContain("Run project MCP server", second.Frames, StringComparison.Ordinal);

        second.Console.SendText("/quit");
        second.Console.SendEnter();
        await secondRun;
    }

    [Fact]
    public async Task Trust_lists_and_clears_the_repository_records()
    {
        using var first = new TrustSession();
        await first.QuestionAsync();
        first.Host.Console.SendText("y");
        await first.Host.WaitUntilAsync(() => first.Host.Session.IsComposed);
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            first.Host,
            "remembered for this repository"
        );

        first.Host.Console.SendText("/trust");
        first.Host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(first.Host, "trust: extensions — ");
        Assert.Contains("trust: extensions — ", first.Host.LastFrame, StringComparison.Ordinal);

        first.Host.Console.SendText("/trust clear");
        first.Host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(first.Host, "trust records cleared");
        await first.QuitAsync();

        using var second = new TrustSession(worktree: first.Worktree, store: first.Store);
        await second.QuestionAsync();

        Assert.NotNull(second.Host.Session.PendingTrustPrompt);
        second.Host.Console.SendText("n");
        await second.Host.WaitUntilAsync(() => second.Host.Session.IsComposed);
        await second.QuitAsync();
    }

    [Fact]
    public async Task A_changed_extension_re_prompts_and_says_the_files_changed()
    {
        using var first = new TrustSession();
        await first.QuestionAsync();
        first.Host.Console.SendText("y");
        await first.Host.WaitUntilAsync(() => first.Host.Session.IsComposed);
        await first.QuitAsync();

        File.WriteAllText(
            Path.Combine(first.Worktree, ".lunate", "extensions", "hello", "changed.txt"),
            "changed"
        );

        using var second = new TrustSession(worktree: first.Worktree, store: first.Store);
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            second.Host,
            "its files changed since this repository trusted it"
        );

        second.Host.Console.SendText("y");
        await second.Host.WaitUntilAsync(() => second.Log.Contains("created hello"));
        await second.QuitAsync();
    }

    /// <summary>
    /// One interactive session over a shared worktree and trust store: the project HelloExtension
    /// is installed untrusted; keys may be queued before the session starts (type-ahead).
    /// </summary>
    private sealed class TrustSession : IDisposable
    {
        public TrustSession(string? worktree = null, string? store = null, string? typeAhead = null)
        {
            Worktree = worktree ?? Temp.File("worktree");
            Store = store ?? Temp.File("store");
            Directory.CreateDirectory(Path.Combine(Worktree, ".git"));
            string extension = Path.Combine(Worktree, ".lunate", "extensions", "hello");
            if (!Directory.Exists(extension))
            {
                HelloFixture.Install(extension);
            }

            Log = new RecordingExtensionLog();
            Host = new InteractiveSessionHost(
                extensions: new ExtensionSeams { StorePath = Store, Log = Log },
                workingDirectory: Worktree
            );
            Host.Console.Size = new ConsoleSize(400, 24);
            if (typeAhead is not null)
            {
                Host.Console.SendText(typeAhead);
            }

            Run = Host.RunAsync();
        }

        public TempDirectory Temp { get; } = new();

        public string Worktree { get; }

        public string Store { get; }

        public RecordingExtensionLog Log { get; }

        public InteractiveSessionHost Host { get; }

        public Task Run { get; }

        public Task QuestionAsync() =>
            InteractiveSessionCommandTests.WaitForFrameAsync(
                Host,
                "Load project extension 'hello'"
            );

        public async Task QuitAsync()
        {
            await Host.WaitUntilAsync(() => Host.Session.IsComposed);
            Host.Console.SendText("/quit");
            Host.Console.SendEnter();
            await Run;
        }

        public void Dispose()
        {
            Host.Dispose();
            Temp.Dispose();
        }
    }
}

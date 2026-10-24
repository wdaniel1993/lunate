using System.Globalization;
using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionCommandTests
{
    [Fact]
    public async Task Every_built_in_command_dispatches_without_a_model_call()
    {
        using var host = new InteractiveSessionHost(secretPrompt: new CancelledSecretPrompt());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/new");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "new session");

        host.Console.SendText("/compact");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "nothing to compact");

        host.Console.SendText("/thinking");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "thinking: off");

        host.Console.SendText("/model");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "Select model");
        Assert.Contains("* gpt-4o-mini (openai)", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains("  gpt-4o (openai)", host.LastFrame, StringComparison.Ordinal);
        host.Console.SendEscape();
        await WaitForFrameGoneAsync(host, "Select model");

        host.Console.SendText("/login groq");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "login cancelled");

        host.Console.SendText("/logout groq");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "no stored key for 'groq'");

        host.Console.SendText("/favorite gpt-4o");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "added favorite: gpt-4o");

        host.Console.SendText("/unfavorite gpt-4o");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "removed favorite: gpt-4o");

        host.Console.SendText("/resume");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "Select session");
        host.Console.SendEscape();
        await WaitForFrameGoneAsync(host, "Select session");

        Assert.Empty(host.Client.Requests);
        Assert.False(host.Session.IsRunning);

        // The last command is recalled by Up; Ctrl+C clears it before the final quit.
        host.Console.Send(new KeyEvent(KeyKind.Up, null, false, false, false));
        await WaitForFrameAsync(host, "> /resume");
        host.Console.SendCtrlC();
        await WaitForFrameGoneAsync(host, "> /resume");

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Help_lists_every_command_grouped_with_its_description()
    {
        // A catalog model plus a scripted client keeps composition independent of the
        // developer's settings and of any provider key. One extension command is present so the
        // listing covers a non-built-in source too; the built-in block below stays byte-pinned.
        using var temp = new TempDirectory();
        string store = temp.File("store");
        CommandFixture.Install(store, settings: CommandFixture.Echo());
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() }
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/help");
        host.Console.SendEnter();
        // "extensions" also appears in the welcome footer; wait on the listing's last line so the
        // wait covers the whole block the asserts compare below.
        await WaitForScrollbackAsync(host, "  /ping — Ping the extension");

        Assert.Contains(
            """
            session
              /clone    — copy the whole session into a new session file
              /compact  — compact the conversation now, optionally focusing on <text>
              /context  — show the context window and subagent budgets
              /fork     — start a new session file at a picked entry
              /help     — list the commands
              /name     — set or show the session title
              /new      — start a fresh session
              /resume   — open the session picker
              /rewind   — move the active leaf to a picked entry
              /session  — show the session file, messages and tokens
              /thinking — set the reasoning level (off|low|medium|high)
              /tree     — browse the session tree and pick a message
            model
              /favorite   — add the active or named model to favorites
              /login      — store an API key for a provider preset
              /logout     — remove the stored API key for a provider preset
              /model      — pick a model or switch to one by id
              /unfavorite — remove the active or named model from favorites
            extensions
              /extensions   — list the discovered extensions with their state
              /mcp          — list the MCP servers with their status and source
              /skill:<name> — invoke a discovered skill by name
              /skills       — list the discovered skills with their scope and state
              /trust        — show or clear this repository's trust records
            quit
              /quit — quit
            """,
            host.ScrollbackText,
            StringComparison.Ordinal
        );
        // The non-built-in section follows the built-in groups (design D1).
        Assert.Contains(
            """
            quit
              /quit — quit
            extensions
              /ping — Ping the extension
            """,
            host.ScrollbackText,
            StringComparison.Ordinal
        );
        Assert.Empty(host.Client.Requests);
        Assert.False(host.Session.IsRunning);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task An_unknown_command_is_a_notice_and_never_reaches_the_model()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/nope");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "unknown command: /nope");

        Assert.Empty(host.Client.Requests);
        Assert.False(host.Session.IsRunning);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Commands_skip_the_input_pipeline()
    {
        var runner = new HookRunner();
        var consumer = new ConsumingHandler();
        runner.Register("ext.test", consumer);
        using var host = new InteractiveSessionHost(hooks: runner);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/new");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "new session");
        Assert.Empty(consumer.Seen);

        host.Console.SendText("hello");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "input consumed by an extension hook");
        Assert.Equal(["hello"], consumer.Seen);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_busy_session_refuses_the_state_changing_commands()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        foreach (string command in new[] { "/new", "/resume", "/model", "/compact" })
        {
            host.Console.SendText(command);
            await host.WaitUntilAsync(() => host.Session.Input.Text == command);
            host.Console.SendEnter();
            await WaitForFrameAsync(host, "slash command cannot be queued");

            Assert.Equal(0, host.Session.QueuedFollowUpCount);
            Assert.Equal(command, host.Session.Input.Text);
            for (var index = 0; index < command.Length; index++)
            {
                host.Console.SendBackspace();
            }

            await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);
        }

        Assert.Contains("slash command cannot be queued", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("Select model", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("Select session", host.LastFrame, StringComparison.Ordinal);
        Assert.True(host.Session.IsRunning);
        Assert.Single(host.Client.Requests);
        string sessions = host.Temp.File("sessions");
        Assert.Single(Directory.GetFiles(sessions, "*.jsonl"));
        Session session = Session.Load(Directory.GetFiles(sessions, "*.jsonl")[0]);
        Assert.Empty(session.Entries.OfType<SessionModelChangeEntry>());

        host.Client.Release(1);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        // Quit is idle-only now; Ctrl+C is the running turn's quit path.
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
        Assert.False(host.Session.IsRunning);
    }

    [Fact]
    public async Task Compact_reports_when_the_history_has_nothing_compactable()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/compact");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "nothing to compact");

        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Compact_summarizes_the_history_older_than_the_kept_tail()
    {
        using var host = new InteractiveSessionHost(configureHarness: options =>
            options with
            {
                CompactionKeepTurns = 1,
            }
        );
        host.Client.Enqueue(Scripts.Text("a1"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("a2"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("a3"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("one");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await WaitForScrollbackAsync(host, "a1");
        host.Console.SendText("two");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        await WaitForScrollbackAsync(host, "a2");

        host.Console.SendText("/compact");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "context compacted");

        string path = Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl"));
        Session session = Session.Load(path);
        SessionCompactionEntry compaction = Assert.Single(
            session.Entries.OfType<SessionCompactionEntry>()
        );
        Assert.Contains("summary of older turns", compaction.Summary, StringComparison.Ordinal);

        // The next request carries the summary instead of the summarized history.
        host.Console.SendText("three");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(3);
        string[] texts =
        [
            .. host
                .Client.Requests[2]
                .Where(message =>
                    message.Role != Microsoft.Extensions.AI.ChatRole.System
                    && !string.IsNullOrEmpty(message.Text)
                )
                .Select(message => message.Text),
        ];
        Assert.Equal(["summary of older turns", "two", "a2", "three"], texts);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Compact_with_focus_passes_the_text_beside_the_fixed_sections()
    {
        using var host = new InteractiveSessionHost(configureHarness: options =>
            options with
            {
                CompactionKeepTurns = 1,
            }
        );
        host.Client.Enqueue(Scripts.Text("a1"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("a2"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await RunTwoTurnsAsync(host);

        host.Console.SendText("/compact focus on the flaky test");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "context compacted");

        IReadOnlyList<ChatMessage> summarization = Assert.Single(host.Client.SummaryRequests);
        string prompt = summarization[0].Text;
        Assert.StartsWith(CompactionReducer.SummarizationPrompt, prompt, StringComparison.Ordinal);
        Assert.Contains("focus on the flaky test", prompt, StringComparison.Ordinal);
        foreach (string section in new[] { "Goal:", "Decisions:", "Files:", "Open problems:" })
        {
            Assert.Contains(section, prompt, StringComparison.Ordinal);
        }

        host.Console.Complete();
        await run;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Compact_without_instructions_uses_only_the_fixed_prompt(string argument)
    {
        using var host = new InteractiveSessionHost(configureHarness: options =>
            options with
            {
                CompactionKeepTurns = 1,
            }
        );
        host.Client.Enqueue(Scripts.Text("a1"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("a2"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await RunTwoTurnsAsync(host);

        host.Console.SendText("/compact" + argument);
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "context compacted");

        IReadOnlyList<ChatMessage> summarization = Assert.Single(host.Client.SummaryRequests);
        Assert.Equal(CompactionReducer.SummarizationPrompt, summarization[0].Text);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Compact_refuses_more_than_a_thousand_characters_without_summarizing()
    {
        using var host = new InteractiveSessionHost(configureHarness: options =>
            options with
            {
                CompactionKeepTurns = 1,
            }
        );
        host.Console.Size = new Lunate.Tui.ConsoleSize(120, 24);
        host.Client.Enqueue(Scripts.Text("a1"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("a2"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await RunTwoTurnsAsync(host);

        host.Console.SendText("/compact " + new string('x', 1001));
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "compact instructions are limited to 1,000 characters");

        Assert.Empty(host.Client.SummaryRequests);
        Session session = Session.Load(
            Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl"))
        );
        Assert.Empty(session.Entries.OfType<SessionCompactionEntry>());
        Assert.Equal(4, session.Entries.OfType<SessionMessageEntry>().Count());
        Assert.False(host.Session.IsRunning);

        host.Console.Complete();
        await run;
    }

    private static async Task RunTwoTurnsAsync(InteractiveSessionHost host)
    {
        host.Console.SendText("one");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await WaitForScrollbackAsync(host, "a1");
        host.Console.SendText("two");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        await WaitForScrollbackAsync(host, "a2");
    }

    [Fact]
    public async Task Command_history_recalls_the_command_text()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/new");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "new session");

        host.Console.Send(new KeyEvent(KeyKind.Up, null, false, false, false));
        await WaitForFrameAsync(host, "> /new");

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Context_lists_the_subagent_limits_and_the_tree_usage()
    {
        using var host = new InteractiveSessionHost();
        host.Console.Size = new Lunate.Tui.ConsoleSize(400, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/context");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "subagents: concurrency 4");

        Assert.Contains("depth 1 (fixed)", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains(
            "tree usage: 0 tokens, 0 running",
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Theory]
    [InlineData(1200L, 300L, "cache: read 1.2k, write 300")]
    [InlineData(1200L, null, "cache: read 1.2k")]
    [InlineData(null, 300L, "cache: write 300")]
    public async Task Context_shows_each_cache_count_the_provider_reported(
        long? cacheRead,
        long? cacheWrite,
        string expected
    )
    {
        using var host = new InteractiveSessionHost();
        host.Console.Size = new Lunate.Tui.ConsoleSize(400, 24);
        host.Client.Enqueue(
            CacheUsage(cacheRead, cacheWrite),
            Scripts.Text("done"),
            Scripts.Stop()
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await RunOneTurnAsync(host);
        host.Console.SendText("/context");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, expected);

        Assert.Contains("15/128.0k", host.LastFrame, StringComparison.Ordinal);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Name_sets_the_title_sidecar()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        string path = Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl"));
        byte[] before = File.ReadAllBytes(path);

        host.Console.SendText("/name   Refactor   the parser  ");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "session name: Refactor the parser");

        Assert.Equal("Refactor the parser", SessionTitle.Read(path));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0L, 0L)]
    public async Task Context_omits_the_cache_segment_when_the_provider_reported_no_cache_counts(
        long? cacheRead,
        long? cacheWrite
    )
    {
        using var host = new InteractiveSessionHost();
        host.Console.Size = new Lunate.Tui.ConsoleSize(400, 24);
        host.Client.Enqueue(
            CacheUsage(cacheRead, cacheWrite),
            Scripts.Text("done"),
            Scripts.Stop()
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await RunOneTurnAsync(host);
        host.Console.SendText("/context");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "subagent");

        Assert.Contains("15/128.0k", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("cache:", host.LastFrame, StringComparison.Ordinal);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Bare_name_reports_the_stored_title()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        SessionTitle.Write(
            Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl")),
            "Refactor the parser"
        );

        host.Console.SendText("/name");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "session name: Refactor the parser");

        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static async Task RunOneTurnAsync(InteractiveSessionHost host)
    {
        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
    }

    private static ChatResponseUpdate CacheUsage(long? cacheRead, long? cacheWrite)
    {
        var usage = new UsageDetails
        {
            InputTokenCount = 10,
            OutputTokenCount = 5,
            CachedInputTokenCount = cacheRead,
        };
        if (cacheWrite is { } write)
        {
            usage.AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["CacheCreationInputTokens"] = write,
            };
        }

        return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(usage)]);
    }

    [Fact]
    public async Task Bare_name_reports_none_without_a_title()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/name");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "session name: (none)");

        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task An_over_long_name_is_a_notice_without_writing()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        string path = Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl"));

        host.Console.SendText("/name " + new string('x', SessionTitle.MaxLength + 1));
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "session name must be 1-120 characters");

        Assert.False(File.Exists(SessionTitle.PathFor(path)));
        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Name_without_a_session_file_is_a_notice()
    {
        using var host = new InteractiveSessionHost(launch: new LaunchOptions { NoSession = true });
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/name Ghost");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "no session file to name");

        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Session_reports_the_path_id_message_count_and_tokens()
    {
        using var host = new InteractiveSessionHost();
        host.Scrollback.Profile.Width = 400;
        host.Client.Enqueue(Usage(1000, 500), Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await WaitForScrollbackAsync(host, "done");
        string path = Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl"));
        Session session = Session.Load(path);

        host.Console.SendText("/session");
        host.Console.SendEnter();
        await WaitForScrollbackAsync(host, "tokens: 1000 in / 500 out");

        Assert.Contains($"session: {path}", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains($"id: {session.SessionId}", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains("messages: 2", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains("cost: $0.0005", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains(
            "gpt-4o-mini: 1000 in / 500 out / 0 cache read / 0 cache write = $0.0005",
            host.ScrollbackText,
            StringComparison.Ordinal
        );
        Assert.Single(host.Client.Requests);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Session_reports_the_cache_lanes_and_cost_of_a_schema_3_turn()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "claude-sonnet-5-5" }
        );
        host.Scrollback.Profile.Width = 400;
        host.Client.Enqueue(
            CacheUsage(input: 1500, output: 50, cacheRead: 1200, cacheWrite: 300),
            Scripts.Text("done"),
            Scripts.Stop()
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await RunOneTurnAsync(host);
        host.Console.SendText("/session");
        host.Console.SendEnter();
        await WaitForScrollbackAsync(host, "tokens: 1500 in / 50 out");

        Assert.Contains(
            "cache: 1200 read / 300 write",
            host.ScrollbackText,
            StringComparison.Ordinal
        );
        Assert.Contains("cost: $0.0014", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains(
            "claude-sonnet-5-5: 1500 in / 50 out / 1200 cache read / 300 cache write = $0.0014",
            host.ScrollbackText,
            StringComparison.Ordinal
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Session_with_one_unpriced_model_prints_tokens_without_any_cost()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("models.json"),
            $$"""
            { "schemaVersion": 2, "models": [ {{PricedModel}}, {{UnpricedModel}} ] }
            """
        );
        string path = temp.File("prewritten.jsonl");
        Session session = Session.Create(path, temp.Root);
        session.AppendMessage(new ChatMessage(ChatRole.User, "hi"));
        session.AppendMessage(
            new ChatMessage(ChatRole.Assistant, "one"),
            "fixture-priced",
            new SessionUsage(1000, 100, 50, 25)
        );
        session.AppendMessage(
            new ChatMessage(ChatRole.Assistant, "two"),
            "fixture-unpriced",
            new SessionUsage(200, 20)
        );
        using var started = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "fixture-priced" },
            startSession: path,
            modelsPath: temp.File("models.json")
        );
        started.Scrollback.Profile.Width = 400;
        Task run = started.RunAsync();
        await started.WaitUntilAsync(() => started.Session.IsComposed);

        started.Console.SendText("/session");
        started.Console.SendEnter();
        await WaitForScrollbackAsync(started, "tokens: 1200 in / 120 out");

        Assert.Contains(
            "cache: 50 read / 25 write",
            started.ScrollbackText,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("cost:", started.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain(" = $", started.ScrollbackText, StringComparison.Ordinal);

        started.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Session_prints_tokens_without_a_cost_for_a_negative_usage_lane()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("models.json"),
            $$"""
            { "schemaVersion": 2, "models": [ {{PricedModel}} ] }
            """
        );
        string path = temp.File("negative-cache.jsonl");
        File.WriteAllText(
            path,
            """
            {"type":"header","schema":3,"id":"s_20261007-090000-b1c2","cwd":"/repo","created":"2026-10-07T09:00:00.0000000+00:00","meai":"10.10.1"}
            {"type":"message","id":"e_01","parentId":null,"timestamp":"2026-10-07T09:00:01.0000000+00:00","message":{"role":"user","contents":[{"$type":"text","text":"hi"}]}}
            {"type":"message","id":"e_02","parentId":"e_01","timestamp":"2026-10-07T09:00:02.0000000+00:00","message":{"role":"assistant","contents":[{"$type":"text","text":"priced."}]},"model":"fixture-priced","usage":{"input":1000,"output":100,"cacheRead":-1}}
            """
        );
        using var started = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "fixture-priced" },
            startSession: path,
            modelsPath: temp.File("models.json")
        );
        started.Scrollback.Profile.Width = 400;
        Task run = started.RunAsync();
        await started.WaitUntilAsync(() => started.Session.IsComposed);

        started.Console.SendText("/session");
        started.Console.SendEnter();
        await WaitForScrollbackAsync(started, "tokens: 1000 in / 100 out");

        Assert.DoesNotContain("cost:", started.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain(" = $", started.ScrollbackText, StringComparison.Ordinal);

        started.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Session_stays_token_only_for_a_schema_2_file_whose_model_has_a_cache_rate()
    {
        using var temp = new TempDirectory();
        string path = temp.File("schema-2.jsonl");
        File.WriteAllText(
            path,
            Schema2Session(model: "claude-sonnet-5-5", input: 1500, output: 50)
        );
        using var started = new InteractiveSessionHost(startSession: path);
        started.Scrollback.Profile.Width = 400;
        Task run = started.RunAsync();
        await started.WaitUntilAsync(() => started.Session.IsComposed);

        started.Console.SendText("/session");
        started.Console.SendEnter();
        await WaitForScrollbackAsync(started, "tokens: 1500 in / 50 out");

        Assert.DoesNotContain("cost:", started.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain("cache:", started.ScrollbackText, StringComparison.Ordinal);

        started.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Session_costs_a_schema_2_file_whose_model_has_no_cache_rate()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("models.json"),
            $$"""
            { "schemaVersion": 2, "models": [ {{OtherPricedModel}} ] }
            """
        );
        string path = temp.File("schema-2.jsonl");
        File.WriteAllText(
            path,
            Schema2Session(model: "fixture-other", input: 1_000_000, output: 0)
        );
        using var started = new InteractiveSessionHost(
            startSession: path,
            modelsPath: temp.File("models.json")
        );
        started.Scrollback.Profile.Width = 400;
        Task run = started.RunAsync();
        await started.WaitUntilAsync(() => started.Session.IsComposed);

        started.Console.SendText("/session");
        started.Console.SendEnter();
        await WaitForScrollbackAsync(started, "tokens: 1000000 in / 0 out");

        Assert.Contains("cost: $1.00", started.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains(
            "fixture-other: 1000000 in / 0 out / 0 cache read / 0 cache write = $1.00",
            started.ScrollbackText,
            StringComparison.Ordinal
        );

        started.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Session_formats_the_priced_report_invariantly_under_de_AT()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-AT");
            using var host = new InteractiveSessionHost();
            host.Scrollback.Profile.Width = 400;
            host.Client.Enqueue(Usage(1000, 500), Scripts.Text("done"), Scripts.Stop());
            Task run = host.RunAsync();
            await host.WaitUntilAsync(() => host.Session.IsComposed);

            host.Console.SendText("go");
            host.Console.SendEnter();
            await host.Client.WaitForCallAsync(1);
            await WaitForScrollbackAsync(host, "done");
            host.Console.SendText("/session");
            host.Console.SendEnter();
            await WaitForScrollbackAsync(host, "tokens: 1000 in / 500 out");

            Assert.Contains("cost: $0.0005", host.ScrollbackText, StringComparison.Ordinal);
            Assert.Contains(
                "gpt-4o-mini: 1000 in / 500 out / 0 cache read / 0 cache write = $0.0005",
                host.ScrollbackText,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain("0,0005", host.ScrollbackText, StringComparison.Ordinal);

            host.Console.Complete();
            await run;
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task Session_without_a_file_reports_nones_and_zeros()
    {
        using var host = new InteractiveSessionHost(launch: new LaunchOptions { NoSession = true });
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/session");
        host.Console.SendEnter();
        await WaitForScrollbackAsync(host, "tokens: 0 in / 0 out");

        Assert.Contains("session: (none)", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains("id: (none)", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains("messages: 0", host.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain("cost:", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);

        host.Console.Complete();
        await run;
    }

    private static ChatResponseUpdate Usage(long input, long output) =>
        new(
            ChatRole.Assistant,
            [
                new UsageContent(
                    new UsageDetails { InputTokenCount = input, OutputTokenCount = output }
                ),
            ]
        );

    private static ChatResponseUpdate CacheUsage(
        long input,
        long output,
        long? cacheRead,
        long? cacheWrite
    )
    {
        var usage = new UsageDetails
        {
            InputTokenCount = input,
            OutputTokenCount = output,
            CachedInputTokenCount = cacheRead,
        };
        if (cacheWrite is { } write)
        {
            usage.AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["CacheCreationInputTokens"] = write,
            };
        }

        return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(usage)]);
    }

    private const string PricedModel = """
        { "id": "fixture-priced", "provider": "fixture", "contextWindow": 1000, "supportsTools": true, "pricing": { "input": 1, "output": 2 } }
        """;

    private const string OtherPricedModel = """
        { "id": "fixture-other", "provider": "fixture", "contextWindow": 1000, "supportsTools": true, "pricing": { "input": 1, "output": 2 } }
        """;

    private const string UnpricedModel = """
        { "id": "fixture-unpriced", "provider": "fixture", "contextWindow": 1000, "supportsTools": true }
        """;

    /// <summary>
    /// A schema-2 session file with one assistant usage carrying input/output only: the shape an
    /// older build wrote before the cache lanes existed.
    /// </summary>
    private static string Schema2Session(string model, long input, long output) =>
        $$$"""
            {"type":"header","schema":2,"id":"s_20261007-090000-b1c2","cwd":"/repo","created":"2026-10-07T09:00:00.0000000+00:00","meai":"10.10.1"}
            {"type":"message","id":"e_01","parentId":null,"timestamp":"2026-10-07T09:00:01.0000000+00:00","message":{"role":"user","contents":[{"$type":"text","text":"hi"}]}}
            {"type":"message","id":"e_02","parentId":"e_01","timestamp":"2026-10-07T09:00:02.0000000+00:00","message":{"role":"assistant","contents":[{"$type":"text","text":"priced."}]},"model":"{{{model}}}","usage":{"input":{{{input}}},"output":{{{output}}}}}

            """;

    internal static async Task WaitForFrameAsync(
        InteractiveSessionHost host,
        string text,
        TimeSpan? timeout = null
    )
    {
        await host.WaitUntilAsync(
            () =>
            {
                host.Advance(33);
                return host.LastFrame.Contains(text, StringComparison.Ordinal);
            },
            timeout
        );
    }

    internal static async Task WaitForFrameGoneAsync(InteractiveSessionHost host, string text)
    {
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.LastFrame.Contains(text, StringComparison.Ordinal);
        });
    }

    internal static async Task WaitForScrollbackAsync(
        InteractiveSessionHost host,
        string text,
        TimeSpan? timeout = null
    )
    {
        try
        {
            await host.WaitUntilAsync(
                () =>
                {
                    host.Advance(33);
                    return host.ScrollbackText.Contains(text, StringComparison.Ordinal)
                        && !host.Session.IsRunning;
                },
                timeout
            );
        }
        catch (TimeoutException exception)
        {
            // A scrollback sighting that never shows its text is otherwise unreadable after the
            // fact: the tail says whether the commit landed at all and what it said instead.
            string scrollback = host.ScrollbackText;
            string tail = scrollback.Length <= 800 ? scrollback : scrollback[^800..];
            throw new TimeoutException(
                $"{exception.Message} scrollback tail: <<<{tail.ReplaceLineEndings("|")}>>>",
                exception
            );
        }
    }

    private sealed class ConsumingHandler : IInputReceivedHandler
    {
        public int Priority => 0;

        public List<string> Seen { get; } = [];

        public ValueTask<InputReceivedResult> HandleAsync(
            InputReceivedPayload payload,
            CancellationToken cancellationToken
        )
        {
            Seen.Add(payload.Text);
            return ValueTask.FromResult<InputReceivedResult>(new InputReceivedResult.Consume());
        }
    }

    /// <summary>A prompt that reports a cancelled read (Enter on an empty line).</summary>
    private sealed class CancelledSecretPrompt : ISecretPrompt
    {
        public Task<string?> ReadAsync(string prompt, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }
}

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
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

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
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("/help");
        host.Console.SendEnter();
        await WaitForScrollbackAsync(host, "extensions");

        Assert.Contains(
            """
            session
              /compact  — compact the conversation now
              /context  — show the context window and subagent budgets
              /help     — list the commands
              /name     — set or show the session title
              /new      — start a fresh session
              /resume   — open the session picker
              /session  — show the session file, messages and tokens
              /thinking — set the reasoning level (off|low|medium|high)
            model
              /model — pick a model or switch to one by id
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

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        var probes = 0;
        foreach (string command in new[] { "/new", "/resume", "/model", "/compact" })
        {
            host.Console.SendText(command);
            host.Console.SendEnter();
            host.Console.SendText("probe");
            host.Console.SendEnter();
            probes++;
            int expected = probes;
            await host.WaitUntilAsync(() => host.Session.QueuedSteeringCount == expected);
        }

        // The probes clear the notice on their way into steering, so refresh it once more.
        host.Console.SendText("/compact");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "a turn is running");

        Assert.Contains("a turn is running", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("Select model", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("Select session", host.LastFrame, StringComparison.Ordinal);
        Assert.True(host.Session.IsRunning);
        Assert.Single(host.Client.Requests);
        string sessions = host.Temp.File("sessions");
        Assert.Single(Directory.GetFiles(sessions, "*.jsonl"));
        Session session = Session.Load(Directory.GetFiles(sessions, "*.jsonl")[0]);
        Assert.Empty(session.Entries.OfType<SessionModelChangeEntry>());

        // Quit works while the turn runs: the run is cancelled and the session ends.
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
    public async Task Command_history_recalls_the_command_text()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

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
        Assert.DoesNotContain("cost:", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Single(host.Client.Requests);

        host.Console.Complete();
        await run;
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
}

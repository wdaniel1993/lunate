using System.Text;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class AgentCompactionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const int Window = 1000;
    private const int ThresholdTokens = Window * 80 / 100;

    [Fact]
    public async Task A_request_over_the_threshold_compacts_once_and_rebuilds_system_plus_summary_plus_tail()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        AppendTurn(session, "one " + new string('x', 4000), "noted one");
        AppendTurn(session, "two", "noted two");
        AppendTurn(session, "three", "noted three");
        AppendTurn(session, "four", "noted four");
        var client = new ScriptedChatClient()
            .Enqueue(Summary("goal: continue the work."))
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        AgentHarness harness = Harness(client, session, systemPrompt: "base rules");

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Equal(2, client.Requests.Count);
        ScriptedRequest summarization = client.Requests[0];
        Assert.Equal(ChatRole.System, summarization.Messages[0].Role);
        Assert.Equal(CompactionReducer.SummarizationPrompt, summarization.Messages[0].Text);
        Assert.Equal(ChatRole.User, summarization.Messages[1].Role);
        Assert.Contains("noted one", summarization.Messages[1].Text, StringComparison.Ordinal);
        Assert.Null(summarization.Options!.Tools);

        CompactionApplied applied = Assert.Single(events.OfType<CompactionApplied>());
        Assert.Equal(["e_01", "e_02"], applied.ReplacedEntryIds);
        Assert.InRange(applied.EstimatedTokensAfter, 1, ThresholdTokens - 1);

        SessionCompactionEntry entry = Assert.Single(
            session.Entries.OfType<SessionCompactionEntry>()
        );
        Assert.Equal("goal: continue the work.", entry.Summary);
        Assert.Equal(["e_01", "e_02"], entry.Replaces);

        ScriptedRequest rebuilt = client.Requests[1];
        Assert.Equal("base rules", rebuilt.Messages[0].Text);
        Assert.Equal(ChatRole.Assistant, rebuilt.Messages[1].Role);
        Assert.Equal("goal: continue the work.", rebuilt.Messages[1].Text);
        Assert.Equal(
            [
                (ChatRole.User, "two"),
                (ChatRole.Assistant, "noted two"),
                (ChatRole.User, "three"),
                (ChatRole.Assistant, "noted three"),
                (ChatRole.User, "four"),
                (ChatRole.Assistant, "noted four"),
                (ChatRole.User, "go"),
            ],
            rebuilt.Messages.Skip(2).Select(message => (message.Role, message.Text))
        );
        Assert.True(EstimateTokens(rebuilt.Messages) < Window * 60 / 100);
    }

    [Fact]
    public async Task The_tail_boundary_expands_so_a_tool_pair_stays_with_its_result()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        session.AppendMessage(new ChatMessage(ChatRole.User, "one " + new string('x', 4000)));
        session.AppendMessage(
            new ChatMessage(
                ChatRole.Assistant,
                [new FunctionCallContent("call-1", "read", new Dictionary<string, object?>())]
            )
        );
        session.AppendMessage(
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "contents")])
        );
        AppendTurn(session, "two", "noted two");
        AppendTurn(session, "three", "noted three");
        AppendTurn(session, "four", "noted four");
        var client = new ScriptedChatClient()
            .Enqueue(Summary("summary"))
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());

        await AgentTestSupport.Run(Harness(client, session));

        SessionCompactionEntry entry = Assert.Single(
            session.Entries.OfType<SessionCompactionEntry>()
        );
        Assert.Equal(["e_01"], entry.Replaces);
        ScriptedRequest rebuilt = client.Requests[1];
        Assert.Equal(ChatRole.Assistant, rebuilt.Messages[1].Role);
        Assert.IsType<FunctionCallContent>(Assert.Single(rebuilt.Messages[1].Contents));
        Assert.Equal(ChatRole.Tool, rebuilt.Messages[2].Role);
        Assert.IsType<FunctionResultContent>(Assert.Single(rebuilt.Messages[2].Contents));
        Assert.Equal(ChatRole.User, rebuilt.Messages[3].Role);
    }

    [Fact]
    public async Task A_history_without_an_older_turn_is_left_unchanged_even_over_the_threshold()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        AppendTurn(session, "one " + new string('x', 4000), "noted one");
        AppendTurn(session, "two", "noted two");
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());

        List<AgentEvent> events = await AgentTestSupport.Run(Harness(client, session));

        Assert.Single(client.Requests);
        Assert.Empty(events.OfType<CompactionApplied>());
        Assert.Empty(session.Entries.OfType<SessionCompactionEntry>());
    }

    [Fact]
    public async Task The_estimate_is_corrected_by_the_last_reported_input_tokens()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        AppendTurn(session, "one", "noted one");
        AppendTurn(session, "two", "noted two");
        AppendTurn(session, "three", "noted three");
        AppendTurn(session, "four", "noted four");
        var client = new ScriptedChatClient()
            .Enqueue(WithUsage("ok", 900, 5))
            .Enqueue(Summary("summary"))
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        AgentHarness harness = Harness(client, session);

        List<AgentEvent> first = await Run(harness, "five");
        Assert.Empty(first.OfType<CompactionApplied>());

        List<AgentEvent> second = await Run(harness, "six");
        Assert.Single(second.OfType<CompactionApplied>());
        Assert.Equal(3, client.Requests.Count);
        SessionCompactionEntry entry = Assert.Single(
            session.Entries.OfType<SessionCompactionEntry>()
        );
        Assert.Equal(["e_01", "e_02", "e_03", "e_04"], entry.Replaces);
    }

    [Fact]
    public async Task Forced_compaction_runs_below_the_threshold_and_is_a_noop_without_new_older_turns()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        AppendTurn(session, "one", "noted one");
        AppendTurn(session, "two", "noted two");
        AppendTurn(session, "three", "noted three");
        AppendTurn(session, "four", "noted four");
        AppendTurn(session, "five", "noted five");
        var client = new ScriptedChatClient()
            .Enqueue(Summary("forced summary"))
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        AgentHarness harness = Harness(client, session);

        Assert.True(await harness.CompactNowAsync(Ct));
        SessionCompactionEntry entry = Assert.Single(
            session.Entries.OfType<SessionCompactionEntry>()
        );
        Assert.Equal("forced summary", entry.Summary);
        Assert.Equal(["e_01", "e_02"], entry.Replaces);

        Assert.False(await harness.CompactNowAsync(Ct));

        List<AgentEvent> events = await Run(harness, "go");
        Assert.Empty(events.OfType<CompactionApplied>());
        Assert.Equal("forced summary", client.Requests[1].Messages[0].Text);
        Assert.Equal(ChatRole.User, client.Requests[1].Messages[1].Role);
    }

    [Fact]
    public async Task An_unknown_window_falls_back_to_the_documented_default_of_128k()
    {
        using var temp = new TempDirectory();
        using var capture = new TraceCapture();
        string unknownId = "unknown-window-" + Guid.NewGuid().ToString("N");
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        AppendTurn(session, "one " + new string('x', 500_000), "noted one");
        AppendTurn(session, "two", "noted two");
        AppendTurn(session, "three", "noted three");
        AppendTurn(session, "four", "noted four");
        var client = new ScriptedChatClient()
            .Enqueue(Summary("summary"))
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        AgentHarness harness = Harness(
            client,
            session,
            modelId: unknownId,
            catalog: TestCatalog.WithWindow(Window)
        );

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Single(events.OfType<CompactionApplied>());
        Assert.Equal(1, CountOccurrences(capture.Text, unknownId));
    }

    [Fact]
    public async Task A_failed_summarization_leaves_the_request_unchanged_and_is_reported()
    {
        using var temp = new TempDirectory();
        using var capture = new TraceCapture();
        string marker = "compact-boom-" + Guid.NewGuid().ToString("N");
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        AppendTurn(session, "one " + new string('x', 4000), "noted one");
        AppendTurn(session, "two", "noted two");
        AppendTurn(session, "three", "noted three");
        AppendTurn(session, "four", "noted four");
        var client = new ScriptedChatClient()
            .EnqueueFailure(new InvalidOperationException(marker))
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        AgentHarness harness = Harness(client, session);

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Empty(events.OfType<CompactionApplied>());
        Assert.Empty(session.Entries.OfType<SessionCompactionEntry>());
        Assert.Contains(marker, capture.Text, StringComparison.Ordinal);
        Assert.Equal(2, client.Requests.Count);
        Assert.Contains(
            client.Requests[1].Messages,
            message => message.Text.StartsWith("one x", StringComparison.Ordinal)
        );
        Assert.IsType<RunFinished>(events[^1]);
    }

    [Fact]
    public async Task The_summarization_usage_is_counted_internally_and_not_emitted()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        AppendTurn(session, "one " + new string('x', 4000), "noted one");
        AppendTurn(session, "two", "noted two");
        AppendTurn(session, "three", "noted three");
        AppendTurn(session, "four", "noted four");
        var client = new ScriptedChatClient()
            .Enqueue(WithUsage("summary", 5000, 10))
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        AgentHarness harness = Harness(client, session);

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.DoesNotContain(
            events.OfType<UsageUpdated>(),
            usage => usage.Usage.InputTokenCount == 5000
        );
        Assert.Equal((5000L, 10L), harness.CompactionUsageTotals);
    }

    [Fact]
    public async Task A_compacted_session_resumes_as_summary_plus_post_messages()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        session.AppendMessage(new ChatMessage(ChatRole.User, "old"));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "old answer"));
        session.AppendCompaction("the old summary", ["e_01", "e_02"]);
        session.AppendMessage(new ChatMessage(ChatRole.User, "after"));
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());

        await AgentTestSupport.Run(Harness(client, session));

        Assert.Equal(
            [
                (ChatRole.Assistant, "the old summary"),
                (ChatRole.User, "after"),
                (ChatRole.User, "go"),
            ],
            client.Requests[0].Messages.Select(message => (message.Role, message.Text))
        );
    }

    [Fact]
    public void The_boundary_expands_backward_over_a_trailing_tool_pair()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "one"),
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "read")]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "contents")]),
            new(ChatRole.User, "two"),
            new(ChatRole.Assistant, "noted two"),
            new(ChatRole.User, "three"),
            new(ChatRole.Assistant, "noted three"),
            new(ChatRole.User, "four"),
            new(ChatRole.Assistant, "noted four"),
            new(ChatRole.User, "five"),
        ];

        Assert.Equal(1, CompactionReducer.FindTailStart(history, keepTurns: 4));
        Assert.Equal(5, CompactionReducer.FindTailStart(history, keepTurns: 3));
        Assert.Equal(0, CompactionReducer.FindTailStart(history, keepTurns: 5));
    }

    [Fact]
    public void The_summarizable_part_drops_system_messages_and_a_lone_previous_summary()
    {
        ChatMessage summary = new(ChatRole.Assistant, "old summary");
        List<ChatMessage> history =
        [
            new(ChatRole.System, "rules"),
            summary,
            new(ChatRole.User, "hi"),
        ];

        Assert.Empty(CompactionReducer.SummarizablePart(history, tailStart: 2, summary));
        Assert.Equal(
            ["old summary", "hi"],
            CompactionReducer
                .SummarizablePart(history, tailStart: 3, currentSummary: null)
                .Select(message => message.Text)
        );
    }

    private static AgentHarness Harness(
        ScriptedChatClient client,
        Session? session = null,
        string? systemPrompt = null,
        string modelId = TestCatalog.ModelId,
        ModelCatalog? catalog = null
    ) =>
        new(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions
            {
                Session = session,
                SystemPrompt = systemPrompt,
                ModelId = modelId,
                ModelCatalog = catalog ?? TestCatalog.WithWindow(Window),
            }
        );

    private static async Task<List<AgentEvent>> Run(AgentHarness harness, string input) =>
        await harness.RunAsync(input, Ct).ToListAsync(Ct);

    private static void AppendTurn(Session session, string user, string assistant)
    {
        session.AppendMessage(new ChatMessage(ChatRole.User, user));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, assistant));
    }

    private static ChatResponseUpdate Summary(string text) => LoopScripts.Text(text);

    private static ChatResponseUpdate WithUsage(string text, long input, long output) =>
        new(
            ChatRole.Assistant,
            [
                new TextContent(text),
                new UsageContent(
                    new UsageDetails { InputTokenCount = input, OutputTokenCount = output }
                ),
            ]
        )
        {
            FinishReason = ChatFinishReason.Stop,
        };

    private static int EstimateTokens(IEnumerable<ChatMessage> messages) =>
        Encoding.UTF8.GetByteCount(
            string.Concat(
                messages
                    .SelectMany(message => message.Contents.OfType<TextContent>())
                    .Select(content => content.Text)
            )
        ) / 4;

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}

using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>
/// The compaction path with reasoning content (design D5): the summary replaces the old turns
/// without ever seeing their reasoning as text, the rebuilt history drops exactly the replaced
/// entries, and the session file keeps everything the append-only log recorded.
/// </summary>
public sealed class AgentCompactionReasoningTests
{
    private const string ModelId = "model-a";
    private const int Window = 1000;
    private const string Summary = "goal: continue the work.";
    private const string ReplacedReasoning = "reasoning of the replaced turn";
    private const string ReplacedSignature = "sig-replaced";
    private const string SurvivingReasoning = "reasoning of the surviving turn";
    private const string SurvivingSignature = "sig-surviving";

    [Fact]
    public async Task Reasoning_of_a_turn_the_summary_replaces_is_dropped_from_the_rebuilt_history()
    {
        using var temp = new TempDirectory();
        Scenario scenario = Build(temp);

        List<AgentEvent> events = await AgentTestSupport.Run(scenario.Harness);

        CompactionApplied applied = Assert.Single(events.OfType<CompactionApplied>());
        Assert.Equal(["e_01", "e_02"], applied.ReplacedEntryIds);
        Assert.Equal(2, scenario.Client.Requests.Count);
        ScriptedRequest rebuilt = scenario.Client.Requests[1];
        Assert.DoesNotContain(
            rebuilt.Messages,
            message =>
                message
                    .Contents.OfType<TextReasoningContent>()
                    .Any(content => content.Text == ReplacedReasoning)
        );
        Assert.DoesNotContain(rebuilt.Messages, message => message.Text == "noted one");
    }

    [Fact]
    public async Task A_surviving_assistant_entry_keeps_its_reasoning_in_the_rebuilt_history()
    {
        using var temp = new TempDirectory();
        Scenario scenario = Build(temp);

        await AgentTestSupport.Run(scenario.Harness);

        ChatMessage surviving = Assert.Single(
            scenario.Client.Requests[1].Messages,
            message => message.Text == "noted three"
        );
        TextReasoningContent reasoning = Assert.Single(
            surviving.Contents.OfType<TextReasoningContent>()
        );
        Assert.Equal(SurvivingReasoning, reasoning.Text);
        Assert.Equal(SurvivingSignature, reasoning.ProtectedData);
    }

    [Fact]
    public async Task The_rebuilt_history_is_the_summary_and_the_verbatim_tail_in_order()
    {
        using var temp = new TempDirectory();
        Scenario scenario = Build(temp);

        await AgentTestSupport.Run(scenario.Harness);

        Assert.Equal(
            [
                (ChatRole.System, "base rules"),
                (ChatRole.Assistant, Summary),
                (ChatRole.User, "two"),
                (ChatRole.Assistant, "noted two"),
                (ChatRole.User, "three"),
                (ChatRole.Assistant, "noted three"),
                (ChatRole.User, "four"),
                (ChatRole.Assistant, "noted four"),
                (ChatRole.User, "go"),
            ],
            scenario.Client.Requests[1].Messages.Select(message => (message.Role, message.Text))
        );
    }

    [Fact]
    public async Task The_summarization_transcript_never_contains_reasoning_text()
    {
        using var temp = new TempDirectory();
        Scenario scenario = Build(temp);

        await AgentTestSupport.Run(scenario.Harness);

        ScriptedRequest summarization = scenario.Client.Requests[0];
        Assert.Equal(CompactionReducer.SummarizationPrompt, summarization.Messages[0].Text);
        string transcript = summarization.Messages[1].Text;
        Assert.Contains("noted one", transcript, StringComparison.Ordinal);
        Assert.DoesNotContain(ReplacedReasoning, transcript, StringComparison.Ordinal);
        Assert.DoesNotContain(SurvivingReasoning, transcript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_session_file_keeps_the_replaced_entrys_reasoning_after_compaction()
    {
        using var temp = new TempDirectory();
        Scenario scenario = Build(temp);

        await AgentTestSupport.Run(scenario.Harness);

        SessionCompactionEntry compaction = Assert.Single(
            scenario.Session.Entries.OfType<SessionCompactionEntry>()
        );
        Assert.Equal(["e_01", "e_02"], compaction.Replaces);
        SessionMessageEntry replaced = Assert.Single(
            scenario.Session.Entries.OfType<SessionMessageEntry>(),
            entry => entry.Id == "e_02"
        );
        TextReasoningContent reasoning = Assert.Single(
            replaced.Message.Contents.OfType<TextReasoningContent>()
        );
        Assert.Equal(ReplacedReasoning, reasoning.Text);
        Assert.Equal(ReplacedSignature, reasoning.ProtectedData);
        Assert.Contains(
            ReplacedSignature,
            File.ReadAllText(scenario.Session.Path),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task A_compacted_run_with_reasoning_emits_a_valid_event_sequence()
    {
        using var temp = new TempDirectory();
        Scenario scenario = Build(temp);

        List<AgentEvent> events = await AgentTestSupport.Run(scenario.Harness);

        Assert.Equal(StopReasons.Stop, Assert.IsType<RunFinished>(events[^1]).StopReason);
        Assert.Empty(EventSequenceValidator.Validate(events));
    }

    /// <summary>
    /// Four recorded turns plus the run's own turn with the default keep of four: the first turn
    /// falls inside the replaced part, the third turn (reasoning) survives in the tail.
    /// </summary>
    private static Scenario Build(TempDirectory temp)
    {
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        Append(session, new ChatMessage(ChatRole.User, "one " + new string('x', 4000)));
        Append(session, ReasoningMessage("noted one", ReplacedReasoning, ReplacedSignature));
        Append(session, new ChatMessage(ChatRole.User, "two"));
        Append(session, new ChatMessage(ChatRole.Assistant, "noted two"));
        Append(session, new ChatMessage(ChatRole.User, "three"));
        Append(session, ReasoningMessage("noted three", SurvivingReasoning, SurvivingSignature));
        Append(session, new ChatMessage(ChatRole.User, "four"));
        Append(session, new ChatMessage(ChatRole.Assistant, "noted four"));
        var client = new ScriptedChatClient()
            .Enqueue(LoopScripts.Text(Summary))
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        return new Scenario(session, client, Harness(client, session));
    }

    private static void Append(Session session, ChatMessage message) =>
        session.AppendMessage(message, model: ModelId);

    private static ChatMessage ReasoningMessage(string text, string reasoning, string signature) =>
        new(
            ChatRole.Assistant,
            [
                new TextReasoningContent(reasoning) { ProtectedData = signature },
                new TextContent(text),
            ]
        );

    private static AgentHarness Harness(ScriptedChatClient client, Session session) =>
        new(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions
            {
                Session = session,
                SystemPrompt = "base rules",
                ModelId = ModelId,
                ModelCatalog = TestCatalog.WithWindow(Window, ModelId),
            }
        );

    private sealed record Scenario(
        Session Session,
        ScriptedChatClient Client,
        AgentHarness Harness
    );
}

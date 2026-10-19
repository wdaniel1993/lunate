using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class AgentReasoningTests
{
    [Fact]
    public async Task A_reasoning_only_run_emits_start_content_and_end_in_order()
    {
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Reasoning("Let me "),
            LoopScripts.Reasoning("think."),
            LoopScripts.Stop()
        );
        var harness = new AgentHarness(client, new ToolRegistry());

        List<AgentEvent> events = await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        ReasoningStart start = Assert.IsType<ReasoningStart>(events[1]);
        Assert.Equal("Let me ", Assert.IsType<ReasoningMessageContent>(events[2]).Text);
        Assert.Equal("think.", Assert.IsType<ReasoningMessageContent>(events[3]).Text);
        Assert.Equal(start.MessageId, Assert.IsType<ReasoningEnd>(events[4]).MessageId);
        Assert.Equal(StopReasons.Stop, Assert.IsType<RunFinished>(events[^1]).StopReason);
        Assert.Empty(EventSequenceValidator.Validate(events));
    }

    [Fact]
    public async Task Reasoning_then_text_closes_each_bracket_before_the_next_opens()
    {
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Reasoning("plan"),
            LoopScripts.Text("answer"),
            LoopScripts.Stop()
        );
        var harness = new AgentHarness(client, new ToolRegistry());

        List<AgentEvent> events = await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                typeof(RunStarted),
                typeof(ReasoningStart),
                typeof(ReasoningMessageContent),
                typeof(ReasoningEnd),
                typeof(TextMessageStart),
                typeof(TextMessageContent),
                typeof(TextMessageEnd),
                typeof(RunFinished),
            ],
            events.Select(agentEvent => agentEvent.GetType())
        );
        Assert.Empty(EventSequenceValidator.Validate(events));
    }

    [Fact]
    public async Task Alternating_provider_spans_open_a_new_bracket_each_time()
    {
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Reasoning("first"),
            LoopScripts.Text("middle"),
            LoopScripts.Reasoning("second"),
            LoopScripts.Text("end"),
            LoopScripts.Stop()
        );
        var harness = new AgentHarness(client, new ToolRegistry());

        List<AgentEvent> events = await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, events.OfType<ReasoningStart>().Count());
        Assert.Equal(2, events.OfType<ReasoningEnd>().Count());
        Assert.Equal(2, events.OfType<TextMessageStart>().Count());
        Assert.Equal(2, events.OfType<TextMessageEnd>().Count());
        Assert.Equal(
            events.OfType<ReasoningStart>().Count(),
            events.OfType<ReasoningStart>().Select(start => start.MessageId).Distinct().Count()
        );
        Assert.Equal(
            events.OfType<TextMessageStart>().Count(),
            events.OfType<TextMessageStart>().Select(start => start.MessageId).Distinct().Count()
        );
        Assert.Empty(EventSequenceValidator.Validate(events));
    }

    [Fact]
    public async Task A_provider_failure_mid_reasoning_closes_the_bracket_before_the_error()
    {
        var client = new ScriptedChatClient().EnqueueFailure(
            new InvalidOperationException("provider exploded"),
            LoopScripts.Reasoning("half a thought")
        );
        var harness = new AgentHarness(client, new ToolRegistry());

        List<AgentEvent> events = await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        ReasoningStart start = Assert.Single(events.OfType<ReasoningStart>());
        ReasoningEnd end = Assert.Single(events.OfType<ReasoningEnd>());
        Assert.Equal(start.MessageId, end.MessageId);
        Assert.Contains("provider exploded", Assert.IsType<RunError>(events[^1]).Message);
        Assert.Empty(EventSequenceValidator.Validate(events));
    }

    [Fact]
    public async Task Protected_data_without_text_opens_no_reasoning_bracket()
    {
        var client = new ScriptedChatClient().Enqueue(
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [new TextReasoningContent(string.Empty) { ProtectedData = "signed" }]
            ),
            LoopScripts.Text("answer"),
            LoopScripts.Stop()
        );
        var harness = new AgentHarness(client, new ToolRegistry());

        List<AgentEvent> events = await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(events.OfType<ReasoningStart>());
        Assert.Empty(events.OfType<ReasoningEnd>());
        Assert.Empty(EventSequenceValidator.Validate(events));
    }

    [Fact]
    public async Task Reasoning_fragments_set_the_emitted_flag_so_a_later_failure_is_not_retried()
    {
        var client = new ScriptedChatClient().EnqueueFailure(
            new HttpRequestException("transient after output"),
            LoopScripts.Reasoning("emitted")
        );
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { RetryBaseDelay = TimeSpan.Zero }
        );

        List<AgentEvent> events = await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(client.Requests);
        Assert.Empty(events.OfType<Retrying>());
        Assert.Single(events.OfType<ReasoningStart>());
        Assert.Single(events.OfType<ReasoningEnd>());
    }

    [Fact]
    public async Task An_inline_model_splits_tagged_text_into_reasoning_and_text_brackets()
    {
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("before"),
            LoopScripts.Text("\u003Cthink>mid\u003C/think>after"),
            LoopScripts.Stop()
        );
        var harness = new AgentHarness(client, new ToolRegistry(), InlineOptions());

        List<AgentEvent> events = await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                typeof(RunStarted),
                typeof(TextMessageStart),
                typeof(TextMessageContent),
                typeof(TextMessageEnd),
                typeof(ReasoningStart),
                typeof(ReasoningMessageContent),
                typeof(ReasoningEnd),
                typeof(TextMessageStart),
                typeof(TextMessageContent),
                typeof(TextMessageEnd),
                typeof(RunFinished),
            ],
            events.Select(agentEvent => agentEvent.GetType())
        );
        Assert.Equal(
            ["before", "after"],
            events.OfType<TextMessageContent>().Select(content => content.Text)
        );
        Assert.Equal("mid", Assert.Single(events.OfType<ReasoningMessageContent>()).Text);
        Assert.Empty(EventSequenceValidator.Validate(events));
    }

    [Fact]
    public async Task Tag_fragments_split_across_updates_still_bracket_reasoning()
    {
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("a\u003Cthi"),
            LoopScripts.Text("nk>b\u003C/th"),
            LoopScripts.Text("ink>c"),
            LoopScripts.Stop()
        );
        var harness = new AgentHarness(client, new ToolRegistry(), InlineOptions());

        List<AgentEvent> events = await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["a", "c"],
            events.OfType<TextMessageContent>().Select(content => content.Text)
        );
        Assert.Equal("b", Assert.Single(events.OfType<ReasoningMessageContent>()).Text);
        Assert.Single(events.OfType<ReasoningStart>());
        Assert.Single(events.OfType<ReasoningEnd>());
        Assert.Empty(EventSequenceValidator.Validate(events));
    }

    [Fact]
    public async Task An_unclosed_inline_tag_closes_as_reasoning_at_stream_end()
    {
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("q\u003Cthink>unclosed"),
            LoopScripts.Stop()
        );
        var harness = new AgentHarness(client, new ToolRegistry(), InlineOptions());

        List<AgentEvent> events = await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("q", Assert.Single(events.OfType<TextMessageContent>()).Text);
        Assert.Equal("unclosed", Assert.Single(events.OfType<ReasoningMessageContent>()).Text);
        Assert.Single(events.OfType<ReasoningStart>());
        Assert.Single(events.OfType<ReasoningEnd>());
        Assert.Empty(EventSequenceValidator.Validate(events));
    }

    [Fact]
    public async Task A_model_without_the_inline_style_keeps_tagged_text_verbatim()
    {
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("\u003Cthink>raw\u003C/think>"),
            LoopScripts.Stop()
        );
        var harness = new AgentHarness(client, new ToolRegistry());

        List<AgentEvent> events = await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            "\u003Cthink>raw\u003C/think>",
            Assert.Single(events.OfType<TextMessageContent>()).Text
        );
        Assert.Empty(events.OfType<ReasoningStart>());
        Assert.Empty(EventSequenceValidator.Validate(events));
    }

    private static AgentHarnessOptions InlineOptions() =>
        new() { ModelCatalog = TestCatalog.WithReasoning("inline"), ModelId = TestCatalog.ModelId };
}

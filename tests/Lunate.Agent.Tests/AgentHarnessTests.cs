using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class AgentHarnessTests
{
    [Fact]
    public void Options_default_to_fifty_steps_and_the_current_directory()
    {
        var options = new AgentHarnessOptions();

        Assert.Equal(50, options.MaxSteps);
        Assert.Null(options.SystemPrompt);
        Assert.Equal(Environment.CurrentDirectory, options.WorkingDirectory);
        Assert.Null(options.Approver);
    }

    [Fact]
    public async Task Text_only_run_emits_start_text_and_finish_in_order()
    {
        var client = new ScriptedChatClient().Enqueue(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hello ")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("world")]),
            new ChatResponseUpdate(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.Stop }
        );
        var harness = new AgentHarness(client, new ToolRegistry());

        List<AgentEvent> events = await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        RunStarted started = Assert.IsType<RunStarted>(events[0]);
        Assert.All(events, agentEvent => Assert.Equal(started.RunId, agentEvent.RunId));
        TextMessageStart textStart = Assert.IsType<TextMessageStart>(events[1]);
        Assert.Equal("Hello ", Assert.IsType<TextMessageContent>(events[2]).Text);
        Assert.Equal("world", Assert.IsType<TextMessageContent>(events[3]).Text);
        Assert.Equal(textStart.MessageId, Assert.IsType<TextMessageEnd>(events[4]).MessageId);
        RunFinished finished = Assert.IsType<RunFinished>(events[5]);
        Assert.Equal(StopReasons.Stop, finished.StopReason);
        Assert.Equal(6, events.Count);
        Assert.Empty(EventSequenceValidator.Validate(events));
    }

    [Fact]
    public async Task Text_only_run_appends_the_aggregated_assistant_message()
    {
        var client = new ScriptedChatClient()
            .Enqueue(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hello ")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("world")]),
                new ChatResponseUpdate(ChatRole.Assistant, [])
                {
                    FinishReason = ChatFinishReason.Stop,
                }
            )
            .Enqueue(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Again")]),
                new ChatResponseUpdate(ChatRole.Assistant, [])
                {
                    FinishReason = ChatFinishReason.Stop,
                }
            );
        var harness = new AgentHarness(client, new ToolRegistry());

        await harness
            .RunAsync("one", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);
        await harness
            .RunAsync("two", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        ScriptedRequest second = client.Requests[1];
        Assert.Equal(
            [ChatRole.User, ChatRole.Assistant, ChatRole.User],
            second.Messages.Select(message => message.Role)
        );
        Assert.Equal("Hello world", second.Messages[1].Text);
    }

    [Fact]
    public async Task System_prompt_is_sent_first_when_configured()
    {
        var client = new ScriptedChatClient().Enqueue(StopUpdate());
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { SystemPrompt = "You are Lunate." }
        );

        await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        ScriptedRequest request = client.Requests[0];
        Assert.Equal(2, request.Messages.Count);
        Assert.Equal(ChatRole.System, request.Messages[0].Role);
        Assert.Equal("You are Lunate.", request.Messages[0].Text);
        Assert.Equal(ChatRole.User, request.Messages[1].Role);
    }

    [Fact]
    public async Task No_system_message_is_sent_when_the_prompt_is_null()
    {
        var client = new ScriptedChatClient().Enqueue(StopUpdate());
        var harness = new AgentHarness(client, new ToolRegistry());

        await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        ScriptedRequest request = client.Requests[0];
        Assert.Equal(ChatRole.User, Assert.Single(request.Messages).Role);
    }

    [Fact]
    public async Task Registered_tool_declarations_reach_the_request()
    {
        var tools = new ToolRegistry();
        tools.Add(new ScriptedTool("read", "Reads a file.", """{"type":"object"}"""));
        var client = new ScriptedChatClient().Enqueue(StopUpdate());
        var harness = new AgentHarness(client, tools);

        await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["read"],
            client.Requests[0].Options!.Tools!.Select(declaration => declaration.Name)
        );
    }

    [Fact]
    public async Task A_throwing_provider_emits_run_error_and_completes_the_stream()
    {
        var client = new ScriptedChatClient().EnqueueFailure(
            new InvalidOperationException("provider exploded")
        );
        var harness = new AgentHarness(client, new ToolRegistry());

        List<AgentEvent> events = await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.IsType<RunStarted>(events[0]);
        RunError error = Assert.IsType<RunError>(events[1]);
        Assert.Contains("provider exploded", error.Message);
        Assert.Equal(2, events.Count);
        Assert.Empty(EventSequenceValidator.Validate(events));
    }

    private static ChatResponseUpdate StopUpdate() =>
        new(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.Stop };
}

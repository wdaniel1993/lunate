using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class AgentReasoningRequestFilterTests
{
    private const string Signed = "signed-thinking-block";

    [Fact]
    public async Task A_history_message_from_the_same_model_keeps_its_reasoning_in_the_request()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        session.AppendMessage(ReasoningMessage(), model: "model-a");
        ScriptedChatClient client = new ScriptedChatClient().Enqueue(LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = session, ModelId = "model-a" }
        );

        await harness
            .RunAsync("go", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        TextReasoningContent reasoning = Assert.Single(
            RequestAssistantMessage(client).Contents.OfType<TextReasoningContent>()
        );
        Assert.Equal("a thought", reasoning.Text);
        Assert.Equal(Signed, reasoning.ProtectedData);

        string file = File.ReadAllText(session.Path);
        Assert.Contains("\"$type\":\"reasoning\"", file, StringComparison.Ordinal);
        Assert.Contains(Signed, file, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_history_message_from_another_model_loses_its_reasoning_in_the_request()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        ChatMessage seeded = ReasoningMessage();
        session.AppendMessage(seeded, model: "model-b");
        ScriptedChatClient client = new ScriptedChatClient().Enqueue(LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = session, ModelId = "model-a" }
        );

        await harness
            .RunAsync("go", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        ChatMessage assistant = RequestAssistantMessage(client);
        Assert.Empty(assistant.Contents.OfType<TextReasoningContent>());
        Assert.Equal("visible answer", assistant.Text);

        string file = File.ReadAllText(session.Path);
        Assert.Contains("\"$type\":\"reasoning\"", file, StringComparison.Ordinal);
        Assert.Contains(Signed, file, StringComparison.Ordinal);
        Assert.Single(seeded.Contents.OfType<TextReasoningContent>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_history_message_without_a_usable_recorded_model_keeps_its_reasoning_in_the_request(
        string? recordedModel
    )
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        session.AppendMessage(ReasoningMessage(), model: recordedModel);
        ScriptedChatClient client = new ScriptedChatClient().Enqueue(LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = session, ModelId = "model-a" }
        );

        await harness
            .RunAsync("go", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(RequestAssistantMessage(client).Contents.OfType<TextReasoningContent>());
    }

    [Fact]
    public async Task A_run_without_a_known_current_model_keeps_its_reasoning_in_the_request()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        session.AppendMessage(ReasoningMessage(), model: "model-b");
        ScriptedChatClient client = new ScriptedChatClient().Enqueue(LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = session }
        );

        await harness
            .RunAsync("go", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(RequestAssistantMessage(client).Contents.OfType<TextReasoningContent>());
    }

    [Fact]
    public async Task The_last_resolved_model_id_decides_the_next_request_after_a_route()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        session.AppendMessage(ReasoningMessage(), model: "model-a");
        ScriptedChatClient client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call-1", "read"),
                new ChatResponseUpdate(ChatRole.Assistant, [])
                {
                    FinishReason = ChatFinishReason.ToolCalls,
                    ModelId = "model-b",
                }
            )
            .Enqueue(LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            AgentTestSupport.Registry(AgentTestSupport.ReadTool("contents")),
            new AgentHarnessOptions { Session = session, ModelId = "model-a" }
        );

        await harness
            .RunAsync("go", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, client.Requests.Count);
        Assert.Contains(
            client.Requests[0].Messages,
            message => message.Contents.OfType<TextReasoningContent>().Any()
        );
        Assert.DoesNotContain(
            client.Requests[1].Messages,
            message => message.Contents.OfType<TextReasoningContent>().Any()
        );
    }

    [Fact]
    public async Task Cross_model_stripping_preserves_the_other_contents_of_the_message()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        session.AppendMessage(
            new ChatMessage(
                ChatRole.Assistant,
                [
                    new TextReasoningContent("a thought") { ProtectedData = Signed },
                    new FunctionCallContent(
                        "call-1",
                        "read",
                        new Dictionary<string, object?> { ["path"] = "a.txt" }
                    ),
                ]
            ),
            model: "model-b"
        );
        ScriptedChatClient client = new ScriptedChatClient().Enqueue(LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = session, ModelId = "model-a" }
        );

        await harness
            .RunAsync("go", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        ChatMessage assistant = RequestAssistantMessage(client);
        Assert.Empty(assistant.Contents.OfType<TextReasoningContent>());
        FunctionCallContent call = Assert.Single(assistant.Contents.OfType<FunctionCallContent>());
        Assert.Equal("call-1", call.CallId);
        Assert.Equal("read", call.Name);
    }

    private static ChatMessage ReasoningMessage() =>
        new(
            ChatRole.Assistant,
            [
                new TextReasoningContent("a thought") { ProtectedData = Signed },
                new TextContent("visible answer"),
            ]
        );

    private static ChatMessage RequestAssistantMessage(ScriptedChatClient client) =>
        Assert.Single(
            Assert.Single(client.Requests).Messages,
            message => message.Role == ChatRole.Assistant
        );
}

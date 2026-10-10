using Microsoft.Extensions.AI;

namespace Lunate.Ai.Tests;

public sealed class AnthropicTurnMergeTests
{
    private static string FixturesDirectory =>
        Path.Combine(TestPaths.FindRepositoryRoot(), "tests", "fixtures", "streams");

    [Fact]
    public void Consecutive_tool_and_user_messages_merge_into_one_wire_user_turn()
    {
        IReadOnlyList<ChatMessage> merged = AnthropicTurnMerge.Merge([
            new ChatMessage(ChatRole.User, "question"),
            AssistantCall("call-1"),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "contents")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-2", "more")]),
            new ChatMessage(ChatRole.User, "steer now"),
        ]);

        Assert.Equal(3, merged.Count);
        Assert.Equal(ChatRole.User, merged[0].Role);
        Assert.Equal("question", merged[0].Text);
        Assert.Equal(ChatRole.Assistant, merged[1].Role);
        Assert.Equal(ChatRole.Tool, merged[2].Role);
        Assert.Collection(
            merged[2].Contents,
            content => Assert.IsType<FunctionResultContent>(content),
            content => Assert.IsType<FunctionResultContent>(content),
            content => Assert.Equal("steer now", Assert.IsType<TextContent>(content).Text)
        );
    }

    [Fact]
    public void Consecutive_user_texts_merge_into_one_turn()
    {
        IReadOnlyList<ChatMessage> merged = AnthropicTurnMerge.Merge([
            new ChatMessage(ChatRole.User, "one"),
            new ChatMessage(ChatRole.User, "two"),
        ]);

        ChatMessage only = Assert.Single(merged);
        Assert.Equal(ChatRole.User, only.Role);
        Assert.Equal("onetwo", only.Text);
    }

    [Fact]
    public void Assistant_and_system_messages_are_untouched()
    {
        ChatMessage system = new(ChatRole.System, "You are Lunate.");
        ChatMessage assistant = AssistantCall("call-1");
        IReadOnlyList<ChatMessage> merged = AnthropicTurnMerge.Merge([
            system,
            new ChatMessage(ChatRole.User, "hi"),
            assistant,
        ]);

        Assert.Same(system, merged[0]);
        Assert.Same(assistant, merged[2]);
        Assert.Equal("hi", merged[1].Text);
    }

    [Fact]
    public void Merging_does_not_mutate_the_original_messages()
    {
        ChatMessage tool = new(ChatRole.Tool, [new FunctionResultContent("call-1", "contents")]);
        ChatMessage steering = new(ChatRole.User, "steer now");

        IReadOnlyList<ChatMessage> merged = AnthropicTurnMerge.Merge([tool, steering]);

        Assert.Single(merged);
        Assert.NotSame(tool, merged[0]);
        Assert.Equal(2, merged[0].Contents.Count);
        Assert.Single(tool.Contents);
        Assert.Single(steering.Contents);
    }

    [Theory]
    [InlineData("anthropic-basic.jsonl", "anthropic")]
    [InlineData("opencode-go-basic.jsonl", "openai")]
    public async Task A_committed_provider_fixture_replays_through_the_factory(
        string fixtureName,
        string provider
    )
    {
        FixtureDocument document = FixtureFormat.ReadFile(
            Path.Combine(FixturesDirectory, fixtureName)
        );
        var throwing = new ThrowingChatClient();
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: _ => throwing,
            recorderDecorator: _ => new ReplayChatClient(
                Path.Combine(FixturesDirectory, fixtureName)
            )
        );
        IChatClient client = factory.Create(
            new ModelInfo(document.Header.Model, provider, null, 128_000, true)
        );

        List<ChatResponseUpdate> updates = await client
            .GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "Reply with the single word: pong")],
                cancellationToken: TestContext.Current.CancellationToken
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(document.Exchanges);
        Assert.False(throwing.Called);
        Assert.Contains(
            updates.SelectMany(update => update.Contents).OfType<TextContent>(),
            text => !string.IsNullOrWhiteSpace(text.Text)
        );
    }

    [Fact]
    public async Task The_anthropic_path_records_the_merged_request()
    {
        using var temp = new TempDirectory();
        string path = temp.File("merged.jsonl");
        var provider = new ScriptedChatClient().Enqueue(Text("done"));
        var request = new List<ChatMessage>
        {
            new(ChatRole.User, "question"),
            AssistantCall("call-1"),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "contents")]),
            new(ChatRole.User, "steer now"),
        };
        using var environment = new EnvironmentScope(
            ("LUNATE_RECORD", "1"),
            ("LUNATE_RECORD_PATH", path)
        );
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: _ => provider
        );
        IChatClient client = factory.Create(
            new ModelInfo("claude-sonnet-5-5", "anthropic", null, 128_000, true)
        );

        await client
            .GetStreamingResponseAsync(
                request,
                cancellationToken: TestContext.Current.CancellationToken
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        FixtureDocument document = FixtureFormat.ReadFile(path);
        IReadOnlyList<ChatMessage> merged = AnthropicTurnMerge.Merge(request);
        Assert.Equal(
            FixtureFormat.ComputeRequestDigest(merged, null),
            Assert.Single(document.Exchanges).RequestDigest
        );
        Assert.NotEqual(
            FixtureFormat.ComputeRequestDigest(request, null),
            Assert.Single(document.Exchanges).RequestDigest
        );
        Assert.Equal(3, provider.Requests[0].Messages.Count);
    }

    [Fact]
    public async Task The_openai_path_sends_the_unmerged_request()
    {
        var provider = new ScriptedChatClient().Enqueue(Text("done"));
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: _ => provider
        );
        IChatClient client = factory.Create(
            new ModelInfo("gpt-4o-mini", "openai", null, 128_000, true)
        );

        await client
            .GetStreamingResponseAsync(
                [
                    new ChatMessage(ChatRole.User, "question"),
                    AssistantCall("call-1"),
                    new ChatMessage(
                        ChatRole.Tool,
                        [new FunctionResultContent("call-1", "contents")]
                    ),
                    new ChatMessage(ChatRole.User, "steer now"),
                ],
                cancellationToken: TestContext.Current.CancellationToken
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.User],
            Assert.Single(provider.Requests).Messages.Select(message => message.Role)
        );
    }

    private static ChatMessage AssistantCall(string callId) =>
        new(
            ChatRole.Assistant,
            [
                new FunctionCallContent(
                    callId,
                    "read",
                    new Dictionary<string, object?> { ["path"] = "a.txt" }
                ),
            ]
        );

    private static ChatResponseUpdate Text(string text) =>
        new(ChatRole.Assistant, [new TextContent(text)]);
}

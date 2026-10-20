using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace Lunate.Ai.Tests;

[Collection(ChatClientFactoryEnvCollection.Name)]
public sealed class AnthropicPromptCacheTests
{
    private const string ContentMarkerKey = "anthropic:cache_control";
    private const string ToolMarkerKey = "CacheControl";
    private const string ResponseBody = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-test","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1}}
        """;

    [Fact]
    public async Task The_system_tool_and_latest_blocks_carry_one_marker_each()
    {
        var handler = new CapturingHandler();
        IChatClient client = CreateAnthropicClient(handler);
        ChatMessage system = SystemMessage("rules one", "rules two");
        ChatMessage user = UserMessage("one", "two", "three");
        AITool[] tools = [new EchoTool("echo"), new EchoTool("lookup")];

        await client.GetResponseAsync(
            [system, user],
            new ChatOptions { Tools = [.. tools] },
            TestContext.Current.CancellationToken
        );

        using JsonDocument document = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        JsonElement root = document.RootElement;
        JsonElement[] systemBlocks = [.. root.GetProperty("system").EnumerateArray()];
        JsonElement[] toolBlocks = [.. root.GetProperty("tools").EnumerateArray()];
        JsonElement[] latestBlocks =
        [
            .. root.GetProperty("messages")
                .EnumerateArray()
                .Last()
                .GetProperty("content")
                .EnumerateArray(),
        ];

        Assert.Equal(2, systemBlocks.Length);
        Assert.False(HasCacheControl(systemBlocks[0]));
        Assert.True(HasCacheControl(systemBlocks[^1]));

        Assert.Equal(2, toolBlocks.Length);
        Assert.False(HasCacheControl(toolBlocks[0]));
        Assert.True(HasCacheControl(toolBlocks[^1]));

        Assert.Equal(3, latestBlocks.Length);
        Assert.False(HasCacheControl(latestBlocks[0]));
        Assert.False(HasCacheControl(latestBlocks[1]));
        Assert.True(HasCacheControl(latestBlocks[^1]));

        Assert.Equal(3, CountCacheControls(root));
    }

    [Fact]
    public async Task The_rolling_marker_moves_to_the_latest_block_on_the_next_turn()
    {
        var handler = new CapturingHandler();
        IChatClient client = CreateAnthropicClient(handler);
        ChatMessage system = SystemMessage("rules");
        ChatMessage first = UserMessage("one");
        ChatMessage second = UserMessage("two");
        var tool = new EchoTool("echo");
        ChatOptions options = new() { Tools = [tool] };

        await client.GetResponseAsync(
            [system, first],
            options,
            TestContext.Current.CancellationToken
        );
        await client.GetResponseAsync(
            [system, first, new ChatMessage(ChatRole.Assistant, "answer"), second],
            options,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, handler.RequestBodies.Count);
        using JsonDocument firstDocument = JsonDocument.Parse(handler.RequestBodies[0]);
        using JsonDocument secondDocument = JsonDocument.Parse(handler.RequestBodies[1]);
        JsonElement firstRoot = firstDocument.RootElement;
        JsonElement secondRoot = secondDocument.RootElement;

        Assert.Equal(
            firstRoot.GetProperty("system").GetRawText(),
            secondRoot.GetProperty("system").GetRawText()
        );
        Assert.Equal(
            firstRoot.GetProperty("tools").GetRawText(),
            secondRoot.GetProperty("tools").GetRawText()
        );

        JsonElement[] messages = [.. secondRoot.GetProperty("messages").EnumerateArray()];
        JsonElement[] firstTurn = [.. messages[0].GetProperty("content").EnumerateArray()];
        JsonElement[] latest = [.. messages[^1].GetProperty("content").EnumerateArray()];
        Assert.False(HasCacheControl(firstTurn[^1]));
        Assert.True(HasCacheControl(latest[^1]));
        Assert.Equal(3, CountCacheControls(secondRoot));

        AssertNoContentMarker(first.Contents[^1]);
        AssertNoContentMarker(second.Contents[^1]);
        Assert.False(tool.AdditionalProperties.ContainsKey(ToolMarkerKey));
        Assert.Same(tool, options.Tools![^1]);
    }

    [Fact]
    public async Task Several_blocks_and_tools_never_exceed_the_four_breakpoint_limit()
    {
        var handler = new CapturingHandler();
        IChatClient client = CreateAnthropicClient(handler);
        ChatMessage system = SystemMessage("one", "two", "three");
        ChatMessage user = UserMessage("a", "b", "c", "d");
        AITool[] tools = [new EchoTool("one"), new EchoTool("two"), new EchoTool("three")];

        await client.GetResponseAsync(
            [system, user],
            new ChatOptions { Tools = [.. tools] },
            TestContext.Current.CancellationToken
        );

        using JsonDocument document = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.InRange(CountCacheControls(document.RootElement), 1, 4);
    }

    [Fact]
    public async Task History_markers_above_the_four_breakpoint_limit_reject_the_request()
    {
        var handler = new CapturingHandler();
        IChatClient client = CreateAnthropicClient(handler);
        ChatMessage earlier = UserMessage("one", "two");
        earlier.Contents[0].WithCacheControl(new CacheControlEphemeral());
        earlier.Contents[1].WithCacheControl(new CacheControlEphemeral());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetResponseAsync(
                [SystemMessage("rules"), earlier, UserMessage("three")],
                new ChatOptions { Tools = [new EchoTool("echo")] },
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("4", error.Message, StringComparison.Ordinal);
        Assert.Contains("5", error.Message, StringComparison.Ordinal);
        Assert.Contains("cache_control", error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.RequestBodies);
    }

    [Fact]
    public async Task Exactly_four_breakpoints_including_history_are_sent()
    {
        var handler = new CapturingHandler();
        IChatClient client = CreateAnthropicClient(handler);
        ChatMessage earlier = UserMessage("one");
        earlier.Contents[0].WithCacheControl(new CacheControlEphemeral());

        await client.GetResponseAsync(
            [SystemMessage("rules"), earlier, UserMessage("two")],
            new ChatOptions { Tools = [new EchoTool("echo")] },
            TestContext.Current.CancellationToken
        );

        using JsonDocument document = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.Equal(4, CountCacheControls(document.RootElement));
    }

    [Fact]
    public async Task The_latest_tool_result_block_carries_the_rolling_marker()
    {
        var handler = new CapturingHandler();
        IChatClient client = CreateAnthropicClient(handler);
        ChatMessage assistant = new(
            ChatRole.Assistant,
            [new FunctionCallContent("call_1", "echo", new Dictionary<string, object?>())]
        );
        ChatMessage result = new(ChatRole.Tool, [new FunctionResultContent("call_1", "pong")]);

        await client.GetResponseAsync(
            [SystemMessage("rules"), UserMessage("ping"), assistant, result],
            new ChatOptions { Tools = [new EchoTool("echo")] },
            TestContext.Current.CancellationToken
        );

        using JsonDocument document = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        JsonElement root = document.RootElement;
        JsonElement block = LastWireBlock(root);
        Assert.Equal("tool_result", block.GetProperty("type").GetString());
        Assert.Equal("call_1", block.GetProperty("tool_use_id").GetString());
        Assert.True(HasCacheControl(block));
        Assert.True(HasCacheControl(root.GetProperty("system").EnumerateArray().Last()));
        Assert.True(HasCacheControl(root.GetProperty("tools").EnumerateArray().Last()));
        Assert.Equal(3, CountCacheControls(root));
    }

    [Fact]
    public async Task The_latest_assistant_tool_call_block_carries_the_rolling_marker()
    {
        var handler = new CapturingHandler();
        IChatClient client = CreateAnthropicClient(handler);
        ChatMessage assistant = new(
            ChatRole.Assistant,
            [
                new TextContent("calling"),
                new FunctionCallContent("call_1", "echo", new Dictionary<string, object?>()),
            ]
        );

        await client.GetResponseAsync(
            [SystemMessage("rules"), UserMessage("ping"), assistant],
            new ChatOptions { Tools = [new EchoTool("echo")] },
            TestContext.Current.CancellationToken
        );

        using JsonDocument document = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        JsonElement root = document.RootElement;
        JsonElement block = LastWireBlock(root);
        Assert.Equal("tool_use", block.GetProperty("type").GetString());
        Assert.Equal("call_1", block.GetProperty("id").GetString());
        Assert.Equal("echo", block.GetProperty("name").GetString());
        Assert.True(HasCacheControl(block));
        Assert.True(HasCacheControl(root.GetProperty("system").EnumerateArray().Last()));
        Assert.True(HasCacheControl(root.GetProperty("tools").EnumerateArray().Last()));
        Assert.Equal(3, CountCacheControls(root));
    }

    [Fact]
    public async Task OpenAI_requests_carry_no_prompt_cache_markers()
    {
        ScriptedChatClient scripted = new ScriptedChatClient().Enqueue(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
        );
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: (_, _) => scripted
        );
        var tool = new EchoTool("echo");

        await factory
            .Create(new ModelInfo("gpt-test", "openai", null, 1_000, true))
            .GetResponseAsync(
                [SystemMessage("rules"), UserMessage("one")],
                new ChatOptions { Tools = [tool] },
                TestContext.Current.CancellationToken
            );

        ScriptedRequest request = Assert.Single(scripted.Requests);
        Assert.NotNull(request.Options);
        Assert.False(request.Options!.Tools![^1].AdditionalProperties.ContainsKey(ToolMarkerKey));
        Assert.All(
            request.Messages.SelectMany(message => message.Contents),
            content => AssertNoContentMarker(content)
        );
    }

    private static readonly ModelInfo AnthropicModel = new(
        "claude-test",
        "anthropic",
        null,
        1_000,
        true
    );

    private static IChatClient CreateAnthropicClient(CapturingHandler handler) =>
        new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: (model, _) =>
                new AnthropicClient
                {
                    ApiKey = "test-key",
                    MaxRetries = 0,
                    HttpClient = new HttpClient(handler)
                    {
                        BaseAddress = new Uri("http://localhost"),
                    },
                }.AsIChatClient(model.Id)
        ).Create(AnthropicModel);

    private static ChatMessage SystemMessage(params string[] blocks) =>
        new(ChatRole.System, blocks.Select(block => new TextContent(block)).ToList<AIContent>());

    private static ChatMessage UserMessage(params string[] blocks) =>
        new(ChatRole.User, blocks.Select(block => new TextContent(block)).ToList<AIContent>());

    private static JsonElement LastWireBlock(JsonElement root) =>
        root.GetProperty("messages")
            .EnumerateArray()
            .Last()
            .GetProperty("content")
            .EnumerateArray()
            .Last();

    private static bool HasCacheControl(JsonElement block) =>
        block.TryGetProperty("cache_control", out JsonElement marker)
        && marker.GetProperty("type").GetString() == "ephemeral";

    private static int CountCacheControls(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element
                .EnumerateObject()
                .Sum(property =>
                    (property.NameEquals("cache_control") ? 1 : 0)
                    + CountCacheControls(property.Value)
                ),
            JsonValueKind.Array => element.EnumerateArray().Sum(CountCacheControls),
            _ => 0,
        };

    private static void AssertNoContentMarker(AIContent content) =>
        Assert.False(content.AdditionalProperties?.ContainsKey(ContentMarkerKey) ?? false);

    private sealed class EchoTool(string name) : AIFunction
    {
        private static readonly JsonElement Schema = JsonDocument
            .Parse("""{"type":"object","properties":{"text":{"type":"string"}}}""")
            .RootElement.Clone();

        public override string Name => name;

        public override string Description => "Echoes the given text back.";

        public override JsonElement JsonSchema => Schema;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken
        ) => new("pong");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.Content is { } content)
            {
                RequestBodies.Add(await content.ReadAsStringAsync(cancellationToken));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}

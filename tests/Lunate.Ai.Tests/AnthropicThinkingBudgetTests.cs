using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using Microsoft.Extensions.AI;

namespace Lunate.Ai.Tests;

[Collection(ChatClientFactoryEnvCollection.Name)]
public sealed class AnthropicThinkingBudgetTests
{
    private const string ThinkingStream = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-test","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":0}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"weighing"}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig-1"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: content_block_start
        data: {"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"pong"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":1}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":5}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    [Fact]
    public async Task Budget_style_sends_enabled_thinking_with_the_high_budget_and_max_tokens_above_it()
    {
        (string requestBody, _) = await SendAsync(ThinkingLevel.High);

        using JsonDocument document = JsonDocument.Parse(requestBody);
        JsonElement root = document.RootElement;
        JsonElement thinking = root.GetProperty("thinking");
        Assert.Equal("enabled", thinking.GetProperty("type").GetString());
        Assert.Equal(32_768, thinking.GetProperty("budget_tokens").GetInt64());
        Assert.True(root.GetProperty("max_tokens").GetInt64() > 32_768);
        Assert.Equal("claude-test", root.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Budget_style_sends_the_wire_id_when_it_differs_from_the_catalog_id()
    {
        var model = new ModelInfo(
            "claude-test",
            "anthropic",
            null,
            1_000,
            true,
            AuthRef: null,
            Reasoning: "budget"
        )
        {
            WireId = "claude-test-wire",
        };

        (string requestBody, _) = await SendAsync(ThinkingLevel.High, model: model);

        using JsonDocument document = JsonDocument.Parse(requestBody);
        Assert.Equal("claude-test-wire", document.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task The_thinking_stream_carries_the_text_and_its_signature()
    {
        (_, List<ChatResponseUpdate> updates) = await SendAsync(ThinkingLevel.Medium);

        List<AIContent> contents = [.. updates.SelectMany(update => update.Contents)];
        Assert.Contains(
            contents.OfType<TextReasoningContent>(),
            reasoning => reasoning.Text == "weighing"
        );
        Assert.Contains(
            contents.OfType<TextReasoningContent>(),
            reasoning => reasoning.ProtectedData == "sig-1"
        );
    }

    [Fact]
    public async Task The_assembled_message_carries_one_signed_reasoning_content()
    {
        (_, List<ChatResponseUpdate> updates) = await SendAsync(ThinkingLevel.Medium);

        ChatMessage message = updates.ToChatResponse().Messages[0];
        TextReasoningContent reasoning = Assert.Single(
            message.Contents.OfType<TextReasoningContent>()
        );
        Assert.Equal("weighing", reasoning.Text);
        Assert.Equal("sig-1", reasoning.ProtectedData);
    }

    [Fact]
    public async Task The_request_carries_the_conversation_including_signed_reasoning_and_tools()
    {
        List<ChatMessage> conversation =
        [
            new(ChatRole.User, "Hi"),
            new(
                ChatRole.Assistant,
                [
                    new TextReasoningContent("weighing") { ProtectedData = "sig-1" },
                    new TextContent("visible answer"),
                    new FunctionCallContent("call-1", "echo", new Dictionary<string, object?>()),
                ]
            ),
            new(
                ChatRole.Tool,
                [
                    new FunctionResultContent(
                        "call-1",
                        new Dictionary<string, object?> { ["text"] = "pong" }
                    ),
                ]
            ),
        ];
        (string requestBody, _) = await SendAsync(
            ThinkingLevel.High,
            conversation,
            [new EchoTool()]
        );

        using JsonDocument document = JsonDocument.Parse(requestBody);
        JsonElement root = document.RootElement;
        JsonElement[] messages = [.. root.GetProperty("messages").EnumerateArray()];
        Assert.Equal(3, messages.Length);

        Assert.Equal("Hi", TextOf(messages[0]));

        JsonElement[] assistant = [.. messages[1].GetProperty("content").EnumerateArray()];
        JsonElement thinking = Assert.Single(
            assistant,
            block => block.GetProperty("type").GetString() == "thinking"
        );
        Assert.Equal("weighing", thinking.GetProperty("thinking").GetString());
        Assert.Equal("sig-1", thinking.GetProperty("signature").GetString());
        JsonElement text = Assert.Single(
            assistant,
            block => block.GetProperty("type").GetString() == "text"
        );
        Assert.Equal("visible answer", text.GetProperty("text").GetString());
        JsonElement call = Assert.Single(
            assistant,
            block => block.GetProperty("type").GetString() == "tool_use"
        );
        Assert.Equal("call-1", call.GetProperty("id").GetString());
        Assert.Equal("echo", call.GetProperty("name").GetString());

        JsonElement result = Assert.Single(messages[2].GetProperty("content").EnumerateArray());
        Assert.Equal("tool_result", result.GetProperty("type").GetString());
        Assert.Equal("call-1", result.GetProperty("tool_use_id").GetString());

        JsonElement tool = Assert.Single(root.GetProperty("tools").EnumerateArray());
        Assert.Equal("echo", tool.GetProperty("name").GetString());
        Assert.True(tool.TryGetProperty("input_schema", out _));
    }

    private static string TextOf(JsonElement message) =>
        string.Concat(
            message
                .GetProperty("content")
                .EnumerateArray()
                .Select(block => block.GetProperty("text").GetString())
        );

    private static async Task<(string RequestBody, List<ChatResponseUpdate> Updates)> SendAsync(
        ThinkingLevel level,
        IList<ChatMessage>? messages = null,
        IList<AITool>? tools = null,
        ModelInfo? model = null
    )
    {
        var handler = new CapturingHandler(ThinkingStream);
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: (resolved, _) =>
                new AnthropicClient
                {
                    ApiKey = "test-key",
                    MaxRetries = 0,
                    HttpClient = new HttpClient(handler)
                    {
                        BaseAddress = new Uri("http://localhost"),
                    },
                }.AsIChatClient(resolved.Id)
        );
        model ??= new ModelInfo(
            "claude-test",
            "anthropic",
            null,
            1_000,
            true,
            AuthRef: null,
            Reasoning: "budget"
        );
        ChatOptions options = new();
        if (tools is not null)
        {
            options.Tools = [.. tools];
        }

        ReasoningParameters.Apply(options, model, level);
        IChatClient client = factory.Create(model);

        var updates = new List<ChatResponseUpdate>();
        await foreach (
            ChatResponseUpdate update in client.GetStreamingResponseAsync(
                messages ?? [new ChatMessage(ChatRole.User, "Hi")],
                options,
                TestContext.Current.CancellationToken
            )
        )
        {
            updates.Add(update);
        }

        Assert.NotNull(handler.RequestBody);
        return (handler.RequestBody, updates);
    }

    private sealed class EchoTool : AIFunction
    {
        private static readonly JsonElement Schema = JsonDocument
            .Parse(
                """
                {"type":"object","properties":{"text":{"type":"string"}},"required":["text"],"additionalProperties":false}
                """
            )
            .RootElement.Clone();

        public override string Name => "echo";

        public override string Description => "Echoes the given text back.";

        public override JsonElement JsonSchema => Schema;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken
        ) => new("pong");
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "text/event-stream"),
            };
        }
    }
}

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Ai.Tests;

/// <summary>
/// Live provider contract tests, gated by <c>LUNATE_LIVE=1</c>. Required environment variables:
/// <c>LUNATE_LIVE=1</c> plus the provider key (<c>OPENAI_API_KEY</c> for the OpenAI-compatible path,
/// <c>ANTHROPIC_API_KEY</c> for Anthropic). Without <c>LUNATE_LIVE=1</c> these tests are skipped and
/// never touch the network.
/// </summary>
public sealed class LiveContractTests
{
    private const string LiveVariable = "LUNATE_LIVE";
    private const string OpenAiApiKeyVariable = "OPENAI_API_KEY";
    private const string AnthropicApiKeyVariable = "ANTHROPIC_API_KEY";

    [Fact]
    public async Task OpenAi_streamed_round_trip_returns_meai_updates()
    {
        Assert.SkipUnless(LiveRequested(), $"Live contracts require {LiveVariable}=1.");
        Assert.SkipWhen(
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OpenAiApiKeyVariable)),
            $"{OpenAiApiKeyVariable} is not set."
        );

        await RunStreamedRoundTrip(new ModelInfo("gpt-4o-mini", "openai", null, 128_000, true));
    }

    [Fact]
    public async Task Anthropic_streamed_round_trip_returns_meai_updates()
    {
        Assert.SkipUnless(LiveRequested(), $"Live contracts require {LiveVariable}=1.");
        Assert.SkipWhen(
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AnthropicApiKeyVariable)),
            $"{AnthropicApiKeyVariable} is not set."
        );

        await RunStreamedRoundTrip(
            new ModelInfo("claude-sonnet-5-5", "anthropic", null, 1_000_000, true)
        );
    }

    /// <summary>
    /// The extended-thinking contract: a streaming round-trip with style <c>budget</c> returns
    /// signed thinking blocks, and the tool loop that follows sends them back unchanged (a broken
    /// or missing signature is rejected by the API).
    /// </summary>
    [Fact]
    public async Task Anthropic_signed_thinking_blocks_round_trip_through_a_tool_loop()
    {
        Assert.SkipUnless(LiveRequested(), $"Live contracts require {LiveVariable}=1.");
        Assert.SkipWhen(
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AnthropicApiKeyVariable)),
            $"{AnthropicApiKeyVariable} is not set."
        );

        var model = new ModelInfo(
            "claude-sonnet-5-5",
            "anthropic",
            null,
            1_000_000,
            true,
            AuthRef: null,
            Reasoning: "budget"
        );
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false
        );
        IChatClient client = factory.Create(model);
        ChatOptions options = new() { Tools = [new EchoTool()] };
        ReasoningParameters.Apply(options, model, ThinkingLevel.Medium);
        ChatMessage prompt = new(ChatRole.User, "Use the echo tool to echo the word ping.");

        List<ChatResponseUpdate> first = [];
        await foreach (
            ChatResponseUpdate update in client.GetStreamingResponseAsync(
                [prompt],
                options,
                TestContext.Current.CancellationToken
            )
        )
        {
            first.Add(update);
        }

        ChatMessage assistant = first.ToChatResponse().Messages[0];
        Assert.Contains(
            assistant.Contents.OfType<TextReasoningContent>(),
            reasoning => !string.IsNullOrEmpty(reasoning.Text)
        );
        Assert.Contains(
            assistant.Contents.OfType<TextReasoningContent>(),
            reasoning => !string.IsNullOrEmpty(reasoning.ProtectedData)
        );
        FunctionCallContent call = Assert.Single(assistant.Contents.OfType<FunctionCallContent>());

        var second = new List<ChatResponseUpdate>();
        await foreach (
            ChatResponseUpdate update in client.GetStreamingResponseAsync(
                [
                    prompt,
                    assistant,
                    new ChatMessage(
                        ChatRole.Tool,
                        [new FunctionResultContent(call.CallId!, "pong")]
                    ),
                ],
                options,
                TestContext.Current.CancellationToken
            )
        )
        {
            second.Add(update);
        }

        Assert.NotEmpty(second);
        Assert.NotNull(second[^1].FinishReason);
    }

    private static async Task RunStreamedRoundTrip(ModelInfo model)
    {
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false
        );
        IChatClient client = factory.Create(model);
        var updates = new List<ChatResponseUpdate>();
        await foreach (
            ChatResponseUpdate update in client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "Reply with the single word: pong")],
                cancellationToken: TestContext.Current.CancellationToken
            )
        )
        {
            updates.Add(update);
        }

        Assert.NotEmpty(updates);
        Assert.Contains(
            updates.SelectMany(update => update.Contents).OfType<TextContent>(),
            text => !string.IsNullOrWhiteSpace(text.Text)
        );
        Assert.NotNull(updates[^1].FinishReason);
    }

    private static bool LiveRequested() =>
        string.Equals(
            Environment.GetEnvironmentVariable(LiveVariable),
            "1",
            StringComparison.OrdinalIgnoreCase
        );

    private sealed class EchoTool : AIFunction
    {
        private static readonly JsonElement Schema = JsonDocument.Parse(
            """
            {"type":"object","properties":{"text":{"type":"string"}},"required":["text"],"additionalProperties":false}
            """
        ).RootElement.Clone();

        public override string Name => "echo";

        public override string Description => "Echoes the given text back.";

        public override JsonElement JsonSchema => Schema;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken
        ) => new("pong");
    }
}

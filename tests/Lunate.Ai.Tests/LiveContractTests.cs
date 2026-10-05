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
        Assert.SkipWhen(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OpenAiApiKeyVariable)), $"{OpenAiApiKeyVariable} is not set.");

        await RunStreamedRoundTrip(new ModelInfo("gpt-4o-mini", "openai", null, 128_000, true));
    }

    [Fact]
    public async Task Anthropic_streamed_round_trip_returns_meai_updates()
    {
        Assert.SkipUnless(LiveRequested(), $"Live contracts require {LiveVariable}=1.");
        Assert.SkipWhen(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AnthropicApiKeyVariable)), $"{AnthropicApiKeyVariable} is not set.");

        await RunStreamedRoundTrip(new ModelInfo("claude-sonnet-4.6", "anthropic", null, 200_000, true));
    }

    private static async Task RunStreamedRoundTrip(ModelInfo model)
    {
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }), enableOpenTelemetry: false);
        IChatClient client = factory.Create(model);
        var updates = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Reply with the single word: pong")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        Assert.NotEmpty(updates);
        Assert.Contains(updates.SelectMany(update => update.Contents).OfType<TextContent>(), text => !string.IsNullOrWhiteSpace(text.Text));
        Assert.NotNull(updates[^1].FinishReason);
    }

    private static bool LiveRequested() =>
        string.Equals(Environment.GetEnvironmentVariable(LiveVariable), "1", StringComparison.OrdinalIgnoreCase);
}

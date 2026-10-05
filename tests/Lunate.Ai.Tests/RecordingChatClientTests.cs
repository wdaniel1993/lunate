using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Ai.Tests;

public sealed class RecordingChatClientTests
{
    [Fact]
    public async Task Streaming_passes_updates_through_and_records_the_exchange()
    {
        using var temp = new TempDirectory();
        string path = temp.File("recordings/stream.jsonl");
        List<ChatResponseUpdate> script =
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hello")])
            {
                ModelId = "gpt-4o-mini",
            },
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(" world")])
            {
                ModelId = "gpt-4o-mini",
                FinishReason = ChatFinishReason.Stop,
            },
        ];
        var inner = new ScriptedChatClient().Enqueue([.. script]);
        var client = new RecordingChatClient(inner, path, "gpt-4o-mini");
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        var options = new ChatOptions { ModelId = "gpt-4o-mini" };

        List<ChatResponseUpdate> replayed = await client
            .GetStreamingResponseAsync(messages, options, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
        Assert.Equal(Serialize(script), Serialize(replayed));
        FixtureDocument document = FixtureFormat.ReadFile(path);
        Assert.Equal("gpt-4o-mini", document.Header.Model);
        FixtureExchange exchange = Assert.Single(document.Exchanges);
        Assert.Equal(FixtureFormat.ComputeRequestDigest(messages, options), exchange.RequestDigest);
        Assert.Equal(Serialize(script), Serialize(exchange.Updates));
    }

    [Fact]
    public async Task Each_exchange_is_appended_without_repeating_the_header()
    {
        using var temp = new TempDirectory();
        string path = temp.File("stream.jsonl");
        var inner = new ScriptedChatClient()
            .Enqueue(new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("one")]))
            .Enqueue(new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("two")]));
        var client = new RecordingChatClient(inner, path, "gpt-4o-mini");

        await client
            .GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "first")],
                cancellationToken: TestContext.Current.CancellationToken
            )
            .ToListAsync(TestContext.Current.CancellationToken);
        await client
            .GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "second")],
                cancellationToken: TestContext.Current.CancellationToken
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        FixtureDocument document = FixtureFormat.ReadFile(path);
        Assert.Equal(2, document.Exchanges.Count);
        Assert.NotEqual(document.Exchanges[0].RequestDigest, document.Exchanges[1].RequestDigest);
        Assert.Equal(3, File.ReadAllLines(path).Length);
    }

    [Fact]
    public async Task Header_uses_the_supplied_creation_time()
    {
        using var temp = new TempDirectory();
        string path = temp.File("stream.jsonl");
        DateTimeOffset recordedAt = new(2026, 10, 5, 12, 34, 56, TimeSpan.Zero);
        var inner = new ScriptedChatClient().Enqueue(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("one")])
        );
        var client = new RecordingChatClient(
            inner,
            path,
            "gpt-4o-mini",
            new FixedTimeProvider(recordedAt)
        );

        await client
            .GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "first")],
                cancellationToken: TestContext.Current.CancellationToken
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            recordedAt,
            DateTimeOffset.Parse(
                FixtureFormat.ReadFile(path).Header.RecordedAt,
                System.Globalization.CultureInfo.InvariantCulture
            )
        );
    }

    [Fact]
    public async Task A_failed_stream_records_no_exchange()
    {
        using var temp = new TempDirectory();
        string path = temp.File("stream.jsonl");
        var inner = new ScriptedChatClient().EnqueueFailure(
            new InvalidOperationException("provider boom"),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("partial")])
        );
        var client = new RecordingChatClient(inner, path, "gpt-4o-mini");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client
                .GetStreamingResponseAsync(
                    [new ChatMessage(ChatRole.User, "hello")],
                    cancellationToken: TestContext.Current.CancellationToken
                )
                .ToListAsync(TestContext.Current.CancellationToken)
        );

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Aggregated_response_is_recorded_as_updates()
    {
        using var temp = new TempDirectory();
        string path = temp.File("stream.jsonl");
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "stub"))
        {
            ModelId = "gpt-4o-mini",
        };
        var inner = new ScriptedChatClient().Enqueue(response);
        var client = new RecordingChatClient(inner, path, "gpt-4o-mini");
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        var options = new ChatOptions { ModelId = "gpt-4o-mini" };

        ChatResponse returned = await client.GetResponseAsync(
            messages,
            options,
            TestContext.Current.CancellationToken
        );

        Assert.Same(response, returned);
        FixtureExchange exchange = Assert.Single(FixtureFormat.ReadFile(path).Exchanges);
        Assert.Equal(FixtureFormat.ComputeRequestDigest(messages, options), exchange.RequestDigest);
        Assert.Equal("stub", exchange.Updates.ToChatResponse().Text);
    }

    [Fact]
    public void Constructor_throws_for_an_empty_fixture_path()
    {
        var inner = new ScriptedChatClient();

        Assert.Throws<ArgumentException>("fixturePath", () => new RecordingChatClient(inner, " "));
    }

    private static IEnumerable<string> Serialize(IEnumerable<ChatResponseUpdate> updates) =>
        updates.Select(update => JsonSerializer.Serialize(update, FixtureFormat.JsonOptions));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Ai.Tests;

public sealed class ReplayChatClientTests
{
    [Fact]
    public async Task Streaming_replays_recorded_updates_in_order()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        ChatMessage[] first = [new(ChatRole.User, "one")];
        ChatMessage[] second = [new(ChatRole.User, "two")];
        var firstUpdate = new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("first")]) { ModelId = "gpt-4o-mini" };
        var secondUpdate = new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("second")]) { ModelId = "gpt-4o-mini", FinishReason = ChatFinishReason.Stop };
        WriteFixture(path, (first, null, [firstUpdate]), (second, null, [secondUpdate]));
        var client = new ReplayChatClient(path);

        List<ChatResponseUpdate> replayedFirst = await client
            .GetStreamingResponseAsync(first, cancellationToken: TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);
        List<ChatResponseUpdate> replayedSecond = await client
            .GetStreamingResponseAsync(second, cancellationToken: TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Serialize([firstUpdate]), Serialize(replayedFirst));
        Assert.Equal(Serialize([secondUpdate]), Serialize(replayedSecond));
    }

    [Fact]
    public async Task Aggregated_response_is_aggregated_from_the_recorded_updates()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        var options = new ChatOptions { ModelId = "gpt-4o-mini" };
        WriteFixture(
            path,
            (messages, options,
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hello ")]) { ModelId = "gpt-4o-mini" },
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("world")]) { ModelId = "gpt-4o-mini", FinishReason = ChatFinishReason.Stop },
            ]));
        var client = new ReplayChatClient(path);

        ChatResponse response = await client.GetResponseAsync(messages, options, TestContext.Current.CancellationToken);

        Assert.Equal("Hello world", response.Text);
        Assert.Equal("gpt-4o-mini", response.ModelId);
    }

    [Fact]
    public async Task Request_digest_mismatch_fails_actionably()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        ChatMessage[] recorded = [new(ChatRole.User, "hello")];
        ChatMessage[] actual = [new(ChatRole.User, "different")];
        WriteFixture(path, (recorded, null, [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("hi")])]));
        var client = new ReplayChatClient(path);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetStreamingResponseAsync(actual, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));

        string expectedDigest = FixtureFormat.ComputeRequestDigest(recorded, null);
        string actualDigest = FixtureFormat.ComputeRequestDigest(actual, null);
        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("exchange 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expectedDigest[..12], exception.Message, StringComparison.Ordinal);
        Assert.Contains(actualDigest[..12], exception.Message, StringComparison.Ordinal);
        Assert.Contains("re-record", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Requests_out_of_order_fail_actionably()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        ChatMessage[] first = [new(ChatRole.User, "one")];
        ChatMessage[] second = [new(ChatRole.User, "two")];
        WriteFixture(
            path,
            (first, null, [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("first")])]),
            (second, null, [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("second")])]));
        var client = new ReplayChatClient(path);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetStreamingResponseAsync(second, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));

        Assert.Contains("exchange 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exhausted_fixture_fails_actionably()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        WriteFixture(path, (messages, null, [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("hi")])]));
        var client = new ReplayChatClient(path);
        await client.GetStreamingResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetStreamingResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("exhausted", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 exchange", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replaying_with_two_fresh_clients_is_identical()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        WriteFixture(path, (messages, null, [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("hi")]) { ModelId = "m" }]));

        List<ChatResponseUpdate> first = await new ReplayChatClient(path)
            .GetStreamingResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);
        List<ChatResponseUpdate> second = await new ReplayChatClient(path)
            .GetStreamingResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Serialize(first), Serialize(second));
    }

    [Fact]
    public void Constructor_fails_for_an_unsupported_schema_naming_the_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        File.WriteAllText(path, "{\"type\":\"header\",\"schema\":2,\"model\":\"m\",\"recordedAt\":\"2026-10-05T00:00:00+00:00\"}\n");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => new ReplayChatClient(path));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_throws_for_an_empty_fixture_path()
    {
        Assert.Throws<ArgumentException>("fixturePath", () => new ReplayChatClient(" "));
    }

    private static void WriteFixture(string path, params (ChatMessage[] Messages, ChatOptions? Options, ChatResponseUpdate[] Updates)[] exchanges)
    {
        var lines = new List<string> { FixtureFormat.SerializeHeader("gpt-4o-mini", new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero)) };
        foreach ((ChatMessage[] messages, ChatOptions? options, ChatResponseUpdate[] updates) in exchanges)
        {
            lines.Add(FixtureFormat.SerializeExchange(FixtureFormat.ComputeRequestDigest(messages, options), updates));
        }

        File.WriteAllText(path, string.Join("\n", lines) + "\n");
    }

    private static IEnumerable<string> Serialize(IEnumerable<ChatResponseUpdate> updates) =>
        updates.Select(update => JsonSerializer.Serialize(update, FixtureFormat.JsonOptions));
}

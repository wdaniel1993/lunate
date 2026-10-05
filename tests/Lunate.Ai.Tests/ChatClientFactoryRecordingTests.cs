using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Ai.Tests;

public sealed class ChatClientFactoryRecordingTests
{
    private const string RecordVariable = "LUNATE_RECORD";
    private const string RecordPathVariable = "LUNATE_RECORD_PATH";

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public async Task Create_with_LUNATE_RECORD_records_exchanges_at_the_environment_path(string value)
    {
        using var temp = new TempDirectory();
        string path = temp.File("recording.jsonl");
        using var environment = new EnvironmentScope((RecordVariable, value), (RecordPathVariable, path));
        var inner = new ScriptedChatClient().Enqueue(new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("live")]) { ModelId = "gpt-4o-mini" });
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: _ => inner);
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        var options = new ChatOptions { ModelId = "gpt-4o-mini" };

        IChatClient client = factory.Create(TestModel());
        List<ChatResponseUpdate> updates = await client
            .GetStreamingResponseAsync(messages, options, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("live", Assert.Single(updates).Text);
        FixtureDocument document = FixtureFormat.ReadFile(path);
        Assert.Equal("gpt-4o-mini", document.Header.Model);
        FixtureExchange exchange = Assert.Single(document.Exchanges);
        Assert.Equal(FixtureFormat.ComputeRequestDigest(messages, options), exchange.RequestDigest);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData(null)]
    public async Task Create_without_a_truthy_LUNATE_RECORD_does_not_record(string? value)
    {
        using var temp = new TempDirectory();
        string path = temp.File("recording.jsonl");
        using var environment = new EnvironmentScope((RecordVariable, value), (RecordPathVariable, path));
        var inner = new ScriptedChatClient().Enqueue(new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("live")]));
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: _ => inner);

        IChatClient client = factory.Create(TestModel());
        await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Create_explicit_recorder_decorator_wins_over_LUNATE_RECORD()
    {
        using var temp = new TempDirectory();
        string path = temp.File("recording.jsonl");
        using var environment = new EnvironmentScope((RecordVariable, "1"), (RecordPathVariable, path));
        bool decoratorCalled = false;
        var inner = new ScriptedChatClient().Enqueue(new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("live")]));
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: _ => inner,
            recorderDecorator: client =>
            {
                decoratorCalled = true;
                return client;
            });

        IChatClient built = factory.Create(TestModel());
        await built.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        Assert.True(decoratorCalled);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Create_uses_LUNATE_RECORD_PATH_over_the_default_path()
    {
        using var temp = new TempDirectory();
        string path = temp.File("custom/location.jsonl");
        using var environment = new EnvironmentScope((RecordVariable, "1"), (RecordPathVariable, path));
        var inner = new ScriptedChatClient().Enqueue(new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("live")]));
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: _ => inner);

        IChatClient client = factory.Create(TestModel());
        await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Create_records_raw_fragments_and_replay_assembles_them()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fragments.jsonl");
        var provider = new ScriptedChatClient().Enqueue(
            FragmentUpdate("call-1", "list_files", "{\"path\":\""),
            FragmentUpdate("call-1", string.Empty, "a.txt\"}"));
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        var recordingFactory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: _ => provider,
            recorderDecorator: inner => new RecordingChatClient(inner, path, "gpt-4o-mini"));

        IChatClient recording = recordingFactory.Create(TestModel());
        await recording
            .GetStreamingResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        string fixture = File.ReadAllText(path);
        Assert.Contains(StreamAccumulator.ArgumentsFragmentKey, fixture, StringComparison.Ordinal);

        var replayFactory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: _ => new ThrowingChatClient(),
            recorderDecorator: _ => new ReplayChatClient(path));
        IChatClient replay = replayFactory.Create(TestModel());
        List<ChatResponseUpdate> updates = await replay
            .GetStreamingResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        FunctionCallContent call = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
        Assert.Equal("call-1", call.CallId);
        Assert.Equal("list_files", call.Name);
        Assert.Equal("a.txt", Unwrap(call.Arguments!["path"]));
    }

    [Fact]
    public void Default_recording_path_is_under_artifacts_recordings_and_gitignored()
    {
        string path = ChatClientFactory.DefaultRecordingPath().Replace('\\', '/');

        Assert.Matches(@"^artifacts/recordings/\d{8}-\d{6}-[0-9a-f]{8}\.jsonl$", path);
        string[] ignoreLines = File.ReadAllLines(Path.Combine(TestPaths.FindRepositoryRoot(), ".gitignore"));
        Assert.Contains(ignoreLines, line => line.Trim().TrimEnd('/') == "artifacts");
    }

    [Fact]
    public void Default_recording_paths_are_unique_within_the_same_second()
    {
        string[] paths = [.. Enumerable.Range(0, 32).Select(_ => ChatClientFactory.DefaultRecordingPath().Replace('\\', '/'))];

        Assert.Equal(paths.Length, paths.Distinct(StringComparer.Ordinal).Count());
    }

    private static ChatResponseUpdate FragmentUpdate(string callId, string name, string json) =>
        new(
            ChatRole.Assistant,
            [new FunctionCallContent(callId, name, new Dictionary<string, object?> { [StreamAccumulator.ArgumentsFragmentKey] = json })])
        {
            ModelId = "gpt-4o-mini",
        };

    private static object? Unwrap(object? value) => value is JsonElement element ? element.GetString() : value;

    private static ModelInfo TestModel() => new("gpt-4o-mini", "openai", null, 128_000, true);
}

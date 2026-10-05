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
    public void Default_recording_path_is_under_artifacts_recordings_and_gitignored()
    {
        string path = ChatClientFactory.DefaultRecordingPath().Replace('\\', '/');

        Assert.Matches(@"^artifacts/recordings/\d{8}-\d{6}\.jsonl$", path);
        string[] ignoreLines = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), ".gitignore"));
        Assert.Contains(ignoreLines, line => line.Trim().TrimEnd('/') == "artifacts");
    }

    private static ModelInfo TestModel() => new("gpt-4o-mini", "openai", null, 128_000, true);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "lunate.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Could not find lunate.sln above {AppContext.BaseDirectory}.");
    }
}

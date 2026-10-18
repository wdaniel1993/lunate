using Microsoft.Extensions.AI;

namespace Lunate.Ai.Tests;

/// <summary>
/// The <c>LUNATE_REPLAY</c> seam: a fixture-only pipeline that resolves no credential and
/// constructs no provider client, with a recording request taking precedence when both are set.
/// </summary>
public sealed class ChatClientFactoryReplayTests
{
    private const string ReplayVariable = "LUNATE_REPLAY";
    private const string RecordVariable = "LUNATE_RECORD";
    private const string RecordPathVariable = "LUNATE_RECORD_PATH";
    private const string OpenAiApiKeyVariable = "OPENAI_API_KEY";

    [Fact]
    public async Task Create_with_LUNATE_REPLAY_serves_the_fixture_through_the_pipeline()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        using var environment = new EnvironmentScope(
            (ReplayVariable, path),
            (RecordVariable, null),
            (RecordPathVariable, null),
            (OpenAiApiKeyVariable, null)
        );
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        var options = new ChatOptions { ModelId = "gpt-4o-mini" };
        WriteFixture(path, messages, options, Update("from the fixture"));
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: (_, _) => new ScriptedChatClient().Enqueue(Update("live"))
        );

        IChatClient client = factory.Create(TestModel());
        List<ChatResponseUpdate> updates = await client
            .GetStreamingResponseAsync(messages, options, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("from the fixture", Assert.Single(updates).Text);
    }

    [Fact]
    public async Task Create_with_LUNATE_REPLAY_never_constructs_a_provider_client()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        // No credential is set anywhere: a run that resolved one would fail before serving.
        using var environment = new EnvironmentScope(
            (ReplayVariable, path),
            (RecordVariable, null),
            (RecordPathVariable, null),
            (OpenAiApiKeyVariable, null)
        );
        bool providerRequested = false;
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        var options = new ChatOptions { ModelId = "gpt-4o-mini" };
        WriteFixture(path, messages, options, Update("from the fixture"));
        // Telemetry on: the wrapped pipeline is the one a live run exercises.
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: true,
            providerClientFactory: (_, _) =>
            {
                providerRequested = true;
                return new ThrowingChatClient();
            }
        );

        IChatClient client = factory.Create(TestModel());
        List<ChatResponseUpdate> updates = await client
            .GetStreamingResponseAsync(messages, options, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("from the fixture", Assert.Single(updates).Text);
        Assert.False(providerRequested);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task Create_without_a_replay_path_uses_the_provider_client(string? value)
    {
        using var environment = new EnvironmentScope(
            (ReplayVariable, value),
            (RecordVariable, null),
            (RecordPathVariable, null),
            (OpenAiApiKeyVariable, "test-key")
        );
        var provider = new ScriptedChatClient().Enqueue(Update("live"));
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: (_, _) => provider
        );

        IChatClient client = factory.Create(TestModel());
        List<ChatResponseUpdate> updates = await client
            .GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "hello")],
                cancellationToken: TestContext.Current.CancellationToken
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("live", Assert.Single(updates).Text);
    }

    [Fact]
    public async Task Create_with_LUNATE_RECORD_records_instead_of_replaying()
    {
        using var temp = new TempDirectory();
        string recordingPath = temp.File("recording.jsonl");
        string fixturePath = temp.File("fixture.jsonl");
        using var environment = new EnvironmentScope(
            (RecordVariable, "1"),
            (RecordPathVariable, recordingPath),
            (ReplayVariable, fixturePath)
        );
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        var options = new ChatOptions { ModelId = "gpt-4o-mini" };
        WriteFixture(fixturePath, messages, options, Update("from the fixture"));
        var provider = new ScriptedChatClient().Enqueue(Update("live"));
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: (_, _) => provider
        );

        IChatClient client = factory.Create(TestModel());
        List<ChatResponseUpdate> updates = await client
            .GetStreamingResponseAsync(messages, options, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("live", Assert.Single(updates).Text);
        FixtureDocument recorded = FixtureFormat.ReadFile(recordingPath);
        Assert.Equal(
            FixtureFormat.ComputeRequestDigest(messages, options),
            Assert.Single(recorded.Exchanges).RequestDigest
        );
    }

    [Fact]
    public async Task Create_explicit_recorder_decorator_wins_over_LUNATE_REPLAY()
    {
        using var temp = new TempDirectory();
        // A path that is never read: replaying it would fail before the decorator could run.
        string replayPath = temp.File("missing.jsonl");
        using var environment = new EnvironmentScope(
            (RecordVariable, null),
            (RecordPathVariable, null),
            (ReplayVariable, replayPath)
        );
        bool decoratorCalled = false;
        var provider = new ScriptedChatClient().Enqueue(Update("live"));
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: (_, _) => provider,
            recorderDecorator: client =>
            {
                decoratorCalled = true;
                return client;
            }
        );

        IChatClient client = factory.Create(TestModel());
        List<ChatResponseUpdate> updates = await client
            .GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "hello")],
                cancellationToken: TestContext.Current.CancellationToken
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.True(decoratorCalled);
        Assert.Equal("live", Assert.Single(updates).Text);
    }

    [Fact]
    public void Create_with_an_explicit_credential_does_not_replay()
    {
        using var temp = new TempDirectory();
        string path = temp.File("fixture.jsonl");
        using var environment = new EnvironmentScope(
            (ReplayVariable, path),
            (RecordVariable, null),
            (RecordPathVariable, null)
        );
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        var options = new ChatOptions { ModelId = "gpt-4o-mini" };
        WriteFixture(path, messages, options, Update("from the fixture"));
        var endpoint = new Uri("https://explicit.example.test/v1");
        var model = new ModelInfo("gpt-4o-mini", "openai", endpoint, 128_000, true);
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));

        IChatClient client = factory.Create(model, "explicit-key");

        // ReplayChatClient surfaces no provider metadata; a constructed provider client does.
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));
        Assert.NotNull(metadata);
        Assert.Equal(endpoint, metadata.ProviderUri);
    }

    private static ChatResponseUpdate Update(string text) =>
        new(ChatRole.Assistant, [new TextContent(text)]) { ModelId = "gpt-4o-mini" };

    private static void WriteFixture(
        string path,
        ChatMessage[] messages,
        ChatOptions options,
        params ChatResponseUpdate[] updates
    ) =>
        File.WriteAllLines(
            path,
            [
                FixtureFormat.SerializeHeader("gpt-4o-mini", DateTimeOffset.UnixEpoch),
                FixtureFormat.SerializeExchange(
                    FixtureFormat.ComputeRequestDigest(messages, options),
                    updates
                ),
            ]
        );

    private static ModelInfo TestModel() => new("gpt-4o-mini", "openai", null, 128_000, true);
}

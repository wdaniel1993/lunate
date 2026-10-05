using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace Lunate.Ai.Tests;

public sealed class RecorderReplayEndToEndTests
{
    [Fact]
    public async Task Factory_records_then_replays_identical_streams_with_telemetry_and_logging_active()
    {
        using var temp = new TempDirectory();
        string path = temp.File("pipeline.jsonl");
        bool telemetryObserved = false;
        bool loggingObserved = false;
        using var listener = ListenForTelemetry(() => telemetryObserved = true);
        var loggerFactory = new MarkingLoggerFactory(() => loggingObserved = true);
        var recordingProvider = new ScriptedChatClient();
        foreach (SampleScript.SampleExchange exchange in SampleScript.Exchanges)
        {
            recordingProvider.Enqueue(exchange.Updates);
        }

        var recorded = new List<ChatResponseUpdate>();
        using (
            var recordingEnvironment = new EnvironmentScope(
                ("LUNATE_RECORD", "1"),
                ("LUNATE_RECORD_PATH", path)
            )
        )
        {
            var recordingFactory = new ChatClientFactory(
                loggerFactory,
                enableOpenTelemetry: true,
                providerClientFactory: _ => recordingProvider
            );
            IChatClient recordingClient = recordingFactory.Create(SampleModel());
            foreach (SampleScript.SampleExchange exchange in SampleScript.Exchanges)
            {
                recorded.AddRange(
                    await recordingClient
                        .GetStreamingResponseAsync(
                            exchange.Messages,
                            exchange.Options,
                            TestContext.Current.CancellationToken
                        )
                        .ToListAsync(TestContext.Current.CancellationToken)
                );
            }
        }

        Assert.True(File.Exists(path));
        var throwing = new ThrowingChatClient();
        var replayed = new List<ChatResponseUpdate>();
        using (
            var replayEnvironment = new EnvironmentScope(
                ("LUNATE_RECORD", null),
                ("LUNATE_RECORD_PATH", null)
            )
        )
        {
            var replayFactory = new ChatClientFactory(
                loggerFactory,
                enableOpenTelemetry: true,
                providerClientFactory: _ => throwing,
                recorderDecorator: _ => new ReplayChatClient(path)
            );
            IChatClient replayClient = replayFactory.Create(SampleModel());
            foreach (SampleScript.SampleExchange exchange in SampleScript.Exchanges)
            {
                replayed.AddRange(
                    await replayClient
                        .GetStreamingResponseAsync(
                            exchange.Messages,
                            exchange.Options,
                            TestContext.Current.CancellationToken
                        )
                        .ToListAsync(TestContext.Current.CancellationToken)
                );
            }
        }

        Assert.Equal(
            Serialize(SampleScript.Exchanges.SelectMany(exchange => exchange.Updates)),
            Serialize(replayed)
        );
        Assert.Equal(
            SampleScript.Exchanges.Sum(exchange => exchange.Updates.Length),
            recorded.Count
        );
        Assert.False(throwing.Called);
        Assert.True(telemetryObserved);
        Assert.True(loggingObserved);
    }

    private static ModelInfo SampleModel() =>
        new(SampleScript.ModelId, "openai", null, 128_000, true);

    private static IEnumerable<string> Serialize(IEnumerable<ChatResponseUpdate> updates) =>
        updates.Select(update =>
            System.Text.Json.JsonSerializer.Serialize(update, FixtureFormat.JsonOptions)
        );

    private static ActivityListener ListenForTelemetry(Action onActivity)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                source.Name.Contains("Microsoft.Extensions.AI", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStarted = _ => onActivity(),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}

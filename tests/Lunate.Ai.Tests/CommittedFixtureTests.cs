using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Ai.Tests;

public sealed class CommittedFixtureTests
{
    [Fact]
    public void Committed_fixtures_round_trip_byte_identically()
    {
        string[] files = Directory.GetFiles(FixtureDirectory(), "*.jsonl");
        Assert.NotEmpty(files);
        foreach (string path in files)
        {
            string[] lines = File.ReadAllLines(path);
            FixtureDocument document = FixtureFormat.ReadFile(path);
            DateTimeOffset recordedAt = DateTimeOffset.Parse(
                document.Header.RecordedAt,
                CultureInfo.InvariantCulture
            );
            Assert.Equal(
                FixtureFormat.SerializeHeader(document.Header.Model, recordedAt),
                lines[0]
            );
            for (int index = 0; index < document.Exchanges.Count; index++)
            {
                Assert.Equal(
                    FixtureFormat.SerializeExchange(
                        document.Exchanges[index].RequestDigest,
                        document.Exchanges[index].Updates
                    ),
                    lines[index + 1]
                );
            }

            Assert.Equal(document.Exchanges.Count + 1, lines.Length);
        }
    }

    [Fact]
    public async Task Committed_sample_fixture_exchanges_match_a_fresh_recording_of_the_sample_script()
    {
        using var temp = new TempDirectory();
        string fresh = temp.File(SampleScript.FileName);
        var provider = new ScriptedChatClient();
        foreach (SampleScript.SampleExchange exchange in SampleScript.Exchanges)
        {
            provider.Enqueue(exchange.Updates);
        }

        var recorder = new RecordingChatClient(provider, fresh, SampleScript.HeaderModelId);
        foreach (SampleScript.SampleExchange exchange in SampleScript.Exchanges)
        {
            await recorder
                .GetStreamingResponseAsync(
                    exchange.Messages,
                    exchange.Options,
                    TestContext.Current.CancellationToken
                )
                .ToListAsync(TestContext.Current.CancellationToken);
        }

        string[] committed = File.ReadAllLines(CommittedSamplePath());
        string[] recorded = File.ReadAllLines(fresh);
        FixtureHeader committedHeader = FixtureFormat.ReadFile(CommittedSamplePath()).Header;
        FixtureHeader recordedHeader = FixtureFormat.ReadFile(fresh).Header;
        Assert.Equal(committedHeader.Schema, recordedHeader.Schema);
        Assert.Equal(committedHeader.Model, recordedHeader.Model);
        Assert.Equal(committed.Length, recorded.Length);
        Assert.Equal(committed[1..], recorded[1..]);
    }

    [Fact]
    public async Task Committed_fixture_replays_without_network_or_api_keys()
    {
        var throwing = new ThrowingChatClient();
        using var environment = new EnvironmentScope(
            ("OPENAI_API_KEY", null),
            ("LUNATE_RECORD", null),
            ("LUNATE_RECORD_PATH", null)
        );
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: true,
            providerClientFactory: _ => throwing,
            recorderDecorator: _ => new ReplayChatClient(CommittedSamplePath())
        );
        IChatClient client = factory.Create(
            new ModelInfo(SampleScript.ModelId, "openai", null, 128_000, true)
        );

        var replayed = new List<ChatResponseUpdate>();
        foreach (SampleScript.SampleExchange exchange in SampleScript.Exchanges)
        {
            replayed.AddRange(
                await client
                    .GetStreamingResponseAsync(
                        exchange.Messages,
                        exchange.Options,
                        TestContext.Current.CancellationToken
                    )
                    .ToListAsync(TestContext.Current.CancellationToken)
            );
        }

        Assert.Equal(
            Serialize(SampleScript.Exchanges.SelectMany(exchange => exchange.Updates)),
            Serialize(replayed)
        );
        Assert.False(throwing.Called);
    }

    private static string FixtureDirectory() =>
        Path.Combine(TestPaths.FindRepositoryRoot(), "tests", "fixtures", "streams");

    private static string CommittedSamplePath() =>
        Path.Combine(FixtureDirectory(), SampleScript.FileName);

    private static IEnumerable<string> Serialize(IEnumerable<ChatResponseUpdate> updates) =>
        updates.Select(update => JsonSerializer.Serialize(update, FixtureFormat.JsonOptions));
}

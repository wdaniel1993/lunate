using System.Text.Json;
using Lunate.Ai.Decisions;
using Lunate.Ai.SystemOne;

namespace Lunate.Agent.Tests;

public sealed class SystemOneDecisionClientTransportTests
{
    [Fact]
    public async Task GetDecisionAsync_rejects_a_body_above_32_mib_before_sending()
    {
        using SystemOneRecordingHandler handler = new(
            """{"model":"typesafe/jev-1.14","answers":{"safe":{"type":"noul","noul":0.91}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = CreateClient(httpClient);
        using JsonDocument state = JsonDocument.Parse(
            JsonSerializer.Serialize(new string('x', 32 * 1024 * 1024))
        );

        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetDecisionAsync(
                SystemOneDecisionTestData.CreateRequest(state.RootElement.Clone()),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task GetDecisionAsync_rejects_state_nested_beyond_64_levels()
    {
        using SystemOneRecordingHandler handler = new(
            """{"model":"typesafe/jev-1.14","answers":{"safe":{"type":"noul","noul":0.91}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = CreateClient(httpClient);
        string nested = new string('[', 65) + new string(']', 65);
        using JsonDocument state = JsonDocument.Parse(nested, new JsonDocumentOptions { MaxDepth = 100 });

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetDecisionAsync(
                SystemOneDecisionTestData.CreateRequest(state.RootElement.Clone()),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("64-level", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task GetDecisionAsync_rejects_non_finite_state_numbers()
    {
        using SystemOneRecordingHandler handler = new(
            """{"model":"typesafe/jev-1.14","answers":{"safe":{"type":"noul","noul":0.91}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = CreateClient(httpClient);
        using JsonDocument state = JsonDocument.Parse("""{"quantity":1e9999}""");

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetDecisionAsync(
                SystemOneDecisionTestData.CreateRequest(state.RootElement.Clone()),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("finite", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task GetDecisionAsync_gzips_bodies_above_64_kib()
    {
        using SystemOneRecordingHandler handler = new(
            """{"model":"typesafe/jev-1.14","answers":{"safe":{"type":"noul","noul":0.91}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = CreateClient(httpClient);
        using JsonDocument state = JsonDocument.Parse(
            JsonSerializer.Serialize(new string('x', 65 * 1024))
        );

        await client.GetDecisionAsync(
            SystemOneDecisionTestData.CreateRequest(state.RootElement.Clone()),
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal("gzip", handler.ContentEncoding!.Single());
    }

    [Fact]
    public async Task GetDecisionAsync_retries_429_and_529_before_success()
    {
        using SystemOneTransientHandler handler = new();
        using HttpClient httpClient = new(handler);
        List<TimeSpan> delays = [];
        SystemOneDecisionClient client = new(
            httpClient,
            new SystemOnePreset(new Uri("https://example.test/api/v1/systemone")),
            jitterSource: () => 0,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            }
        );

        DecisionResult result = await client.GetDecisionAsync(
            SystemOneDecisionTestData.CreateRequest(),
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal("yes", result.Results["safe"].Answer);
        Assert.Equal(3, handler.RequestCount);
        Assert.Equal([TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)], delays);
    }

    [Fact]
    public async Task GetDecisionAsync_never_retries_before_retry_after()
    {
        using SystemOneRetryAfterHandler handler = new(TimeSpan.FromSeconds(5));
        using HttpClient httpClient = new(handler);
        TimeSpan? observedDelay = null;
        SystemOneDecisionClient client = new(
            httpClient,
            new SystemOnePreset(new Uri("https://example.test/api/v1/systemone")),
            jitterSource: () => 0,
            delayAsync: (delay, _) =>
            {
                observedDelay = delay;
                return Task.CompletedTask;
            }
        );

        await client.GetDecisionAsync(
            SystemOneDecisionTestData.CreateRequest(),
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal(TimeSpan.FromSeconds(5), observedDelay);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetDecisionAsync_stops_after_three_transient_attempts()
    {
        using SystemOneRateLimitHandler handler = new();
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = new(
            httpClient,
            new SystemOnePreset(new Uri("https://example.test/api/v1/systemone")),
            jitterSource: () => 0,
            delayAsync: (_, _) => Task.CompletedTask
        );

        await Assert.ThrowsAsync<DecisionTransportException>(
            () => client.GetDecisionAsync(
                SystemOneDecisionTestData.CreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(3, handler.RequestCount);
    }

    private static SystemOneDecisionClient CreateClient(HttpClient httpClient) =>
        new(httpClient, new SystemOnePreset(new Uri("https://example.test/api/v1/systemone")));
}

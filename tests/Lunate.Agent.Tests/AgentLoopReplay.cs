using Lunate.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lunate.Agent.Tests;

/// <summary>Records a session through the real pipeline once; replays it without a network.</summary>
internal static class AgentLoopReplay
{
    private const string ModelId = "gpt-4o-mini";

    internal static string FixturesDirectory =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "fixtures", "streams");

    internal static string SnapshotsDirectory =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "fixtures", "agent-loop");

    internal static async Task RecordAsync(AgentLoopSession session, string fixturePath)
    {
        var provider = new ScriptedChatClient();
        foreach (ChatResponseUpdate[] exchange in session.Exchanges)
        {
            provider.Enqueue(exchange);
        }

        using var environment = new EnvironmentScope(
            ("LUNATE_RECORD", "1"),
            ("LUNATE_RECORD_PATH", fixturePath)
        );
        var factory = new ChatClientFactory(
            NullLoggerFactory.Instance,
            enableOpenTelemetry: true,
            providerClientFactory: _ => provider
        );
        var harness = new AgentHarness(factory.Create(Model()), session.CreateTools());
        await harness
            .RunAsync(session.UserInput, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    internal static AgentHarness CreateHarness(AgentLoopSession session)
    {
        IChatClient replay = new ChatClientFactory(
            NullLoggerFactory.Instance,
            enableOpenTelemetry: true,
            providerClientFactory: _ => new ThrowingChatClient(),
            recorderDecorator: _ => new ReplayChatClient(session.FixturePath)
        ).Create(Model());
        return new AgentHarness(replay, session.CreateTools());
    }

    private static ModelInfo Model() => new(ModelId, "openai", null, 128_000, true);
}

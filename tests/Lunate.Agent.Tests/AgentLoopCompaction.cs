using Lunate.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lunate.Agent.Tests;

/// <summary>
/// The recorded long-session acceptance: seeded history older than the kept tail, one run whose
/// first request compacts, recorded once into a committed fixture (recorder itself not committed).
/// </summary>
internal static class AgentLoopCompaction
{
    internal const string Name = "agent-loop-compaction";
    internal const int Window = 4000;
    internal const string UserInput = "five";

    internal static string FixturePath =>
        Path.Combine(AgentLoopReplay.FixturesDirectory, Name + ".jsonl");

    internal static string SnapshotPath =>
        Path.Combine(AgentLoopReplay.SnapshotsDirectory, Name + ".events.txt");

    internal static Session CreateSession(TempDirectory temp)
    {
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        session.AppendMessage(new ChatMessage(ChatRole.User, "one " + new string('x', 20_000)));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "noted one"));
        AppendTurn(session, "two");
        AppendTurn(session, "three");
        AppendTurn(session, "four");
        return session;
    }

    internal static async Task<(ScriptedChatClient Provider, Session Session)> RecordAsync(
        TempDirectory temp,
        string fixturePath
    )
    {
        var provider = new ScriptedChatClient()
            .Enqueue(LoopScripts.Text("goal: continue the recorded long session."))
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        Session session = CreateSession(temp);
        using var environment = new EnvironmentScope(
            ("LUNATE_RECORD", "1"),
            ("LUNATE_RECORD_PATH", fixturePath)
        );
        var factory = new ChatClientFactory(
            NullLoggerFactory.Instance,
            enableOpenTelemetry: true,
            providerClientFactory: _ => provider
        );
        var harness = new AgentHarness(
            factory.Create(Model()),
            new ToolRegistry(),
            Options(session)
        );
        await harness
            .RunAsync(UserInput, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);
        return (provider, session);
    }

    internal static async Task<(List<AgentEvent> Events, Session Session)> ReplayAsync(
        TempDirectory temp
    )
    {
        IChatClient replay = new ChatClientFactory(
            NullLoggerFactory.Instance,
            enableOpenTelemetry: true,
            providerClientFactory: _ => new ThrowingChatClient(),
            recorderDecorator: _ => new ReplayChatClient(FixturePath)
        ).Create(Model());
        Session session = CreateSession(temp);
        var harness = new AgentHarness(replay, new ToolRegistry(), Options(session));
        List<AgentEvent> events = await harness
            .RunAsync(UserInput, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);
        return (events, session);
    }

    private static AgentHarnessOptions Options(Session session) =>
        new()
        {
            Session = session,
            SystemPrompt = "base rules",
            ModelId = TestCatalog.ModelId,
            ModelCatalog = TestCatalog.WithWindow(Window),
        };

    private static ModelInfo Model() => new(TestCatalog.ModelId, "openai", null, Window, true);

    private static void AppendTurn(Session session, string text)
    {
        session.AppendMessage(new ChatMessage(ChatRole.User, text));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "noted " + text));
    }
}

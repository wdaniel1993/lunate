using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class AgentCompactionHookTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const int Window = 1000;

    [Fact]
    public async Task A_hook_provided_summary_overrides_the_default_summarization()
    {
        using var temp = new TempDirectory();
        Session session = SeedLongSession(temp);
        var hooks = new RecordingHookPoints
        {
            Compacting = _ => new AgentCompactingResult.Provide("hooked summary"),
        };
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        AgentHarness harness = Harness(client, session, hooks);

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Single(events.OfType<CompactionApplied>());
        Assert.Single(client.Requests);
        Assert.Equal("hooked summary", client.Requests[0].Messages[0].Text);
        AgentCompactingContext context = Assert.Single(hooks.Compactings);
        Assert.Contains(
            context.Messages,
            message => message.Text.StartsWith("one x", StringComparison.Ordinal)
        );
        SessionCompactionEntry entry = Assert.Single(
            session.Entries.OfType<SessionCompactionEntry>()
        );
        Assert.Equal("hooked summary", entry.Summary);
    }

    [Fact]
    public async Task A_failing_compacting_hook_falls_back_to_the_default_and_is_reported()
    {
        using var temp = new TempDirectory();
        using var capture = new TraceCapture();
        string marker = "hook-boom-" + Guid.NewGuid().ToString("N");
        Session session = SeedLongSession(temp);
        var hooks = new RecordingHookPoints
        {
            Compacting = _ => throw new InvalidOperationException(marker),
        };
        var client = new ScriptedChatClient()
            .Enqueue(LoopScripts.Text("default summary"))
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        AgentHarness harness = Harness(client, session, hooks);

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Single(events.OfType<CompactionApplied>());
        Assert.Equal(2, client.Requests.Count);
        SessionCompactionEntry entry = Assert.Single(
            session.Entries.OfType<SessionCompactionEntry>()
        );
        Assert.Equal("default summary", entry.Summary);
        Assert.Contains(marker, capture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_harness_with_empty_hook_points_behaves_like_one_without_hooks()
    {
        using var temp = new TempDirectory();
        using var tempWithout = new TempDirectory();
        var without = new ScriptedChatClient()
            .Enqueue(LoopScripts.Text("summary"))
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var with = new ScriptedChatClient()
            .Enqueue(LoopScripts.Text("summary"))
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        Session sessionWithout = SeedLongSession(temp);
        Session sessionWith = SeedLongSession(tempWithout);

        List<AgentEvent> withoutEvents = await AgentTestSupport.Run(
            Harness(without, sessionWithout, hooks: null)
        );
        List<AgentEvent> withEvents = await AgentTestSupport.Run(
            Harness(with, sessionWith, hooks: new EmptyHookPoints())
        );

        Assert.Equal(
            withoutEvents.Select(agentEvent => agentEvent.GetType()),
            withEvents.Select(agentEvent => agentEvent.GetType())
        );
        Assert.Equal(
            without.Requests.Select(request => request.Messages.Count),
            with.Requests.Select(request => request.Messages.Count)
        );
        Assert.Equal(
            sessionWithout.Entries.Select(entry => entry.GetType()),
            sessionWith.Entries.Select(entry => entry.GetType())
        );
    }

    [Fact]
    public async Task A_hook_only_fires_when_there_is_something_older_than_the_tail()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        session.AppendMessage(new ChatMessage(ChatRole.User, "one " + new string('x', 4000)));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "noted one"));
        var hooks = new RecordingHookPoints();
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());

        await AgentTestSupport.Run(Harness(client, session, hooks));

        Assert.Empty(hooks.Compactings);
    }

    private static Session SeedLongSession(TempDirectory temp)
    {
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        session.AppendMessage(new ChatMessage(ChatRole.User, "one " + new string('x', 4000)));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "noted one"));
        session.AppendMessage(new ChatMessage(ChatRole.User, "two"));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "noted two"));
        session.AppendMessage(new ChatMessage(ChatRole.User, "three"));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "noted three"));
        session.AppendMessage(new ChatMessage(ChatRole.User, "four"));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "noted four"));
        return session;
    }

    private static AgentHarness Harness(
        ScriptedChatClient client,
        Session session,
        IAgentHookPoints? hooks
    ) =>
        new(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions
            {
                Session = session,
                Hooks = hooks,
                ModelId = TestCatalog.ModelId,
                ModelCatalog = TestCatalog.WithWindow(Window),
            }
        );
}

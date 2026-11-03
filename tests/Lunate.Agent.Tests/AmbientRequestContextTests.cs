using Lunate.Ai;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

/// <summary>
/// The run's request identity: every model call of a run resolves its provider-header templates
/// over the run's own session and run ids — the parent session's on the model calls it makes, and
/// the child session's on the calls a spawned subagent makes (the same rule #473's sessions pin).
/// </summary>
public sealed class AmbientRequestContextTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_run_makes_its_calls_with_its_own_session_and_run_id()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("sessions/parent.jsonl"), temp.Root);
        var seen = new RequestContextSpy(
            new ScriptedChatClient().Enqueue(LoopScripts.Text("done"), LoopScripts.Stop())
        );
        var harness = new AgentHarness(
            seen,
            Registry(),
            new AgentHarnessOptions { Session = session }
        );

        RunStarted start = (await Run(harness)).OfType<RunStarted>().Single();

        Assert.Single(seen.Observed);
        Assert.Equal(session.SessionId, seen.Observed[0].SessionId);
        Assert.Equal(start.RunId, seen.Observed[0].RunId);
    }

    [Fact]
    public async Task A_child_run_makes_its_calls_with_its_own_session_id_and_run_id()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("sessions/parent.jsonl"), temp.Root);
        ScriptedTool spawn = SpawnTool();
        var seen = new RequestContextSpy(
            new ScriptedChatClient()
                .Enqueue(
                    LoopScripts.Call(
                        "call_1",
                        "spawn",
                        LoopScripts.Args(("prompt", "child prompt"))
                    ),
                    LoopScripts.ToolCalls()
                )
                .Enqueue(LoopScripts.Text("child answer"), LoopScripts.Stop())
                .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop())
        );
        var harness = new AgentHarness(
            seen,
            Registry(spawn),
            new AgentHarnessOptions { Session = session }
        );

        List<AgentEvent> events = await Run(harness);
        RunStarted parentStart = events.OfType<RunStarted>().First();
        RunStarted childStart = Assert.Single(
            events.OfType<RunStarted>(),
            start => start.ParentRunId == parentStart.RunId
        );
        Session childSession = Session.Load(
            Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(session.Path))!,
                SessionPaths.SessionFileName(
                    Assert
                        .Single(
                            session.Entries.OfType<SessionChildSessionEntry>(),
                            entry => entry.RunId == childStart.RunId
                        )
                        .ChildSessionId
                )
            )
        );

        // The parent's own model calls name the parent's session; the child's call names the
        // child session and the child run — even though every call shares this one client (the
        // parent makes a second call after the spawn returns).
        Assert.All(
            seen.Observed.Where(context => context.RunId == parentStart.RunId),
            context => Assert.Equal(session.SessionId, context.SessionId)
        );
        Assert.Equal(
            childSession.SessionId,
            Assert.Single(seen.Observed, context => context.RunId == childStart.RunId).SessionId
        );
        Assert.NotEqual(session.SessionId, childSession.SessionId);
    }

    private static ScriptedTool SpawnTool()
    {
        var tool = new ScriptedTool(
            "spawn",
            "Spawns a subagent.",
            """{"type":"object","properties":{"prompt":{"type":"string"}}}""",
            ToolRisk.Write
        );
        tool.OnExecuteAsync = async (device, context, cancellationToken) =>
        {
            SubagentResult result = await context
                .Subagents!.RunAsync(new SubagentRequest("child prompt"), cancellationToken)
                .ConfigureAwait(false);
            return new ToolResult(result.Output, result.IsError);
        };
        return tool;
    }

    /// <summary>Observes the request identity each model call resolves against.</summary>
    private sealed class RequestContextSpy(IChatClient inner) : DelegatingChatClient(inner)
    {
        public List<RequestContext> Observed { get; } = [];

        private void Observe() =>
            Observed.Add(RequestContext.Current ?? new RequestContext(null, null));

        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Observe();
            return base.GetResponseAsync(messages, options, cancellationToken);
        }

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Observe();
            return base.GetStreamingResponseAsync(messages, options, cancellationToken);
        }
    }
}

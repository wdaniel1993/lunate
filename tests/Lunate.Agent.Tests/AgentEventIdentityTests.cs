using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AgentEventIdentityTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_session_attached_run_stamps_every_event()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(ReadTool("contents")),
            new AgentHarnessOptions { Session = session }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.NotEmpty(events);
        Assert.All(events, agentEvent => Assert.Equal(session.SessionId, agentEvent.SessionId));
    }

    [Fact]
    public async Task A_detached_run_leaves_the_session_id_null()
    {
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, new ToolRegistry());

        List<AgentEvent> events = await Run(harness);

        Assert.NotEmpty(events);
        Assert.All(events, agentEvent => Assert.Null(agentEvent.SessionId));
    }

    [Fact]
    public void Base_events_default_to_core_source_and_no_parents()
    {
        var started = new RunStarted("run_1");

        Assert.Equal("core", started.Source);
        Assert.Null(started.ParentRunId);
        Assert.Null(started.SessionId);
    }

    [Fact]
    public void Tool_events_carry_a_parent_tool_call_id()
    {
        const string parent = "call_0";
        var start = new ToolCallStart("run_1", "call_0/1", "read") { ParentToolCallId = parent };
        var args = new ToolCallArgs("run_1", "call_0/1", "{}") { ParentToolCallId = parent };
        var end = new ToolCallEnd("run_1", "call_0/1") { ParentToolCallId = parent };
        var result = new ToolCallResult("run_1", "call_0/1", "out", IsError: false)
        {
            ParentToolCallId = parent,
        };

        Assert.Equal(parent, start.ParentToolCallId);
        Assert.Equal(parent, args.ParentToolCallId);
        Assert.Equal(parent, end.ParentToolCallId);
        Assert.Equal(parent, result.ParentToolCallId);
    }

    [Fact]
    public void Tool_progress_update_carries_run_call_and_message()
    {
        var progress = new ToolProgressUpdate("run_1", "call_1", "half way");

        Assert.Equal(
            ("run_1", "call_1", "half way"),
            (progress.RunId, progress.CallId, progress.Message)
        );
        Assert.IsAssignableFrom<ExtensionEvent>(progress);
    }

    [Fact]
    public async Task The_channel_stamps_only_events_without_a_session_id()
    {
        var channel = new AgentEventChannel("session_1");
        channel.Emit(new RunStarted("run_1"));
        channel.Emit(new RunStarted("run_1") { SessionId = "session_2" });
        channel.Complete();

        List<AgentEvent> received = [];
        await foreach (
            AgentEvent agentEvent in channel.ReadAllAsync(TestContext.Current.CancellationToken)
        )
        {
            received.Add(agentEvent);
        }

        Assert.Equal("session_1", received[0].SessionId);
        Assert.Equal("session_2", received[1].SessionId);
    }

    [Fact]
    public void A_consumer_ignores_an_unknown_extension_event_kind()
    {
        List<AgentEvent> stream =
        [
            new RunStarted("run_1"),
            new UnrecognizedEvent("run_1"),
            new RunFinished("run_1", StopReasons.Stop),
        ];

        List<string> handled = [];
        foreach (AgentEvent agentEvent in stream)
        {
            if (Describe(agentEvent) is { } description)
            {
                handled.Add(description);
            }
        }

        Assert.Equal(["started", "finished"], handled);
    }

    private static string? Describe(AgentEvent agentEvent) =>
        agentEvent switch
        {
            RunStarted => "started",
            RunFinished => "finished",
            _ => null,
        };

    private sealed record UnrecognizedEvent(string RunId) : ExtensionEvent(RunId);
}

namespace Lunate.Agent.Tests;

using System.Text;
using Microsoft.Extensions.AI;

/// <summary>
/// The mid-turn failure contract (issue #338): a continuation model request that fails after tool
/// results joined the conversation still ends the run with a terminal event that names the
/// failure, both when the throw arrives before the stream yields anything and when it arrives
/// after the stream already emitted events.
/// </summary>
public sealed class AgentMidTurnFailureTests
{
    [Fact]
    public async Task A_continuation_request_failure_after_a_tool_result_ends_with_a_run_error()
    {
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call-1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .EnqueueFailure(
                new InvalidOperationException("fixture request mismatch at exchange 2")
            );
        var harness = new AgentHarness(
            client,
            AgentTestSupport.Registry(AgentTestSupport.ReadTool("contents"))
        );

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.False(result.IsError);
        RunError error = Assert.IsType<RunError>(events[^1]);
        Assert.Contains("fixture request mismatch", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task A_failed_turn_leaves_a_complete_history_the_next_request_can_replay()
    {
        // Whatever the failure left behind, the harness's next request is a complete conversation:
        // the session returns to a usable input, not a state the next request cannot replay.
        var client = new ScriptedChatClient()
            .Enqueue(LoopScripts.Call("call-1", "read"), LoopScripts.ToolCalls())
            .EnqueueFailure(
                new InvalidOperationException("dropped mid-turn"),
                LoopScripts.Call("call-2", "read")
            );
        var harness = new AgentHarness(
            client,
            AgentTestSupport.Registry(AgentTestSupport.ReadTool("contents"))
        );

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        Assert.NotEmpty(events.OfType<RunError>());
        Assert.Equal(2, client.Requests.Count);

        // A follow-up turn (the session lets the user resubmit immediately) sees the whole prior
        // history — the executed tool call and its result — and completes.
        client.Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        List<AgentEvent> next = await AgentTestSupport.Run(harness);
        Assert.Empty(EventSequenceValidator.Validate(next));
        Assert.IsType<RunFinished>(next[^1]);
        IReadOnlyList<ChatMessage> followUpRequest = client.Requests[^1].Messages;
        Assert.Equal(2, followUpRequest.Count(message => message.Role == ChatRole.User));
        Assert.Contains(
            followUpRequest,
            message =>
                message
                    .Contents.OfType<FunctionResultContent>()
                    .Any(result => result.CallId == "call-1")
        );
    }

    [Fact]
    public async Task A_secondary_failure_during_the_exit_repair_never_swallows_the_run_error()
    {
        // The dangling-call repair mirrors into the session. A session write that fails while the
        // run's own catch is executing once killed the terminal event, and the consumer saw a
        // bare enumerator exception instead of the failure (issue #338's silent surface): the
        // tool call below is admitted and never repaired because its session append throws.
        using var temp = new TempDirectory();
        string sessionPath = temp.File("s.jsonl");
        Session session = Session.Create(sessionPath, "/work", new FixedTimeProvider(Start));
        session.AppendMessage(new ChatMessage(ChatRole.User, "earlier"));
        var boobyTrap = new StringBuilder();
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Call("call-1", "read"),
            LoopScripts.Call("call-2", "read"),
            LoopScripts.ToolCalls()
        );
        var tool = new ScriptedTool("read", "Reads a file.", """{}""")
        {
            // Replaces the session file with a directory while the second call executes and then
            // faults: every later session append throws, on every platform (a path used as a
            // directory cannot take a sibling file write). The repair's mirror throws on the way
            // out, which must not cost the run its terminal RunError event.
            OnExecute = (args, ctx) =>
            {
                if (boobyTrap.Length == 0)
                {
                    boobyTrap.Append("armed");
                    return new ToolResult("contents", IsError: false);
                }

                File.Delete(sessionPath);
                Directory.CreateDirectory(sessionPath);
                throw new InvalidOperationException("boom");
            },
        };
        var harness = new AgentHarness(
            client,
            AgentTestSupport.Registry(tool),
            new AgentHarnessOptions { Session = session }
        );

        // The harness (not just the stream) must end the run with the terminal RunError event;
        // without the fix this line itself throws with the session write's fault instead.
        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        // The terminal event is the run's answer to the failure — however the exit repair fared,
        // the consumer saw a completed stream ending in RunError instead of a bare exception.
        Assert.IsType<RunError>(events[^1]);
        Assert.NotEmpty(Assert.IsType<RunError>(events[^1]).Message);
    }

    private static readonly DateTimeOffset Start = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
}

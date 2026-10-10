using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AgentSteeringTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Batch_tool_results_stay_adjacent_and_steering_lands_after_the_last_one()
    {
        var steering = new SteeringQueue();
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call-1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.Call("call-2", "read", LoopScripts.Args(("path", "b.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var tool = ReadTool("contents");
        var enqueued = false;
        tool.OnExecute = (_, _) =>
        {
            if (!enqueued)
            {
                enqueued = true;
                steering.Enqueue("steer now");
            }

            return new ToolResult("contents", IsError: false);
        };
        var harness = new AgentHarness(
            client,
            Registry(tool),
            new AgentHarnessOptions { Steering = steering }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        ScriptedRequest second = client.Requests[1];
        Assert.Equal(
            [ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Tool, ChatRole.User],
            second.Messages.Select(message => message.Role)
        );
        Assert.Equal("steer now", second.Messages[^1].Text);

        int lastToolResult = events.FindLastIndex(agentEvent => agentEvent is ToolCallResult);
        int steeringIndex = events.FindIndex(agentEvent => agentEvent is SteeringInjected);
        int nextModelCall = events.FindIndex(agentEvent => agentEvent is TextMessageStart);
        Assert.True(lastToolResult >= 0 && steeringIndex >= 0 && nextModelCall >= 0);
        Assert.True(lastToolResult < steeringIndex);
        Assert.True(steeringIndex < nextModelCall);
    }

    [Fact]
    public async Task Leftover_steering_stays_queued_when_no_model_request_follows()
    {
        var steering = new SteeringQueue();
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Call("call-1", "read", LoopScripts.Args(("path", "a.txt"))),
            LoopScripts.ToolCalls()
        );
        var tool = ReadTool("contents");
        tool.OnExecute = (_, _) =>
        {
            steering.Enqueue("too late");
            return new ToolResult("contents", IsError: false);
        };
        var harness = new AgentHarness(
            client,
            Registry(tool),
            new AgentHarnessOptions { Steering = steering, MaxSteps = 1 }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Equal(StopReasons.StepLimit, Assert.IsType<RunFinished>(events[^1]).StopReason);
        Assert.DoesNotContain(events, agentEvent => agentEvent is SteeringInjected);
        Assert.True(steering.TryDequeue(out string? leftover));
        Assert.Equal("too late", leftover);
        Assert.False(steering.TryDequeue(out _));
    }

    [Fact]
    public async Task A_nested_call_never_drains_the_queue()
    {
        var steering = new SteeringQueue();
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call-1", "outer", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var outer = new ScriptedTool(
            "outer",
            "Runs a nested call.",
            """{"type":"object","properties":{"path":{"type":"string"}}}"""
        )
        {
            OnExecuteAsync = async (args, ctx, ct) =>
            {
                steering.Enqueue("nested must not see this");
                ToolResult nested = await ctx.ExecuteToolAsync!("inner", args, ct);
                return new ToolResult($"outer({nested.Output})", IsError: false);
            },
        };
        var inner = new ScriptedTool(
            "inner",
            "Inner tool.",
            """{"type":"object","properties":{"path":{"type":"string"}}}"""
        )
        {
            OnExecute = (_, _) => new ToolResult("inner ran", IsError: false),
        };
        var harness = new AgentHarness(
            client,
            Registry(outer, inner),
            new AgentHarnessOptions { Steering = steering }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult nestedResult = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.ParentToolCallId is not null
        );
        ToolCallResult outerResult = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.ParentToolCallId is null
        );
        SteeringInjected injected = Assert.Single(events.OfType<SteeringInjected>());
        Assert.True(
            events.IndexOf(nestedResult) < events.IndexOf(outerResult)
                && events.IndexOf(outerResult) < events.IndexOf(injected),
            string.Join(" | ", events.Select(agentEvent => agentEvent.GetType().Name))
        );
    }

    [Fact]
    public async Task Injected_messages_are_mirrored_with_the_parent_chain()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        var steering = new SteeringQueue();
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call-1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var tool = ReadTool("contents");
        tool.OnExecute = (_, _) =>
        {
            steering.Enqueue("first steer");
            steering.Enqueue("second steer");
            return new ToolResult("contents", IsError: false);
        };
        var harness = new AgentHarness(
            client,
            Registry(tool),
            new AgentHarnessOptions { Steering = steering, Session = session }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Equal(
            ["e_04", "e_05"],
            events.OfType<SteeringInjected>().Select(injected => injected.EntryId)
        );
        SessionMessageEntry first = Assert.IsType<SessionMessageEntry>(session.Entries[3]);
        Assert.Equal(ChatRole.User, first.Message.Role);
        Assert.Equal("first steer", first.Message.Text);
        Assert.Equal("e_03", first.ParentId);
        SessionMessageEntry second = Assert.IsType<SessionMessageEntry>(session.Entries[4]);
        Assert.Equal("second steer", second.Message.Text);
        Assert.Equal("e_04", second.ParentId);
        Assert.Empty(EventSequenceValidator.Validate(events));
    }

    [Fact]
    public async Task Without_a_session_the_event_carries_no_entry_id()
    {
        var steering = new SteeringQueue();
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call-1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var tool = ReadTool("contents");
        tool.OnExecute = (_, _) =>
        {
            steering.Enqueue("steer");
            return new ToolResult("contents", IsError: false);
        };
        var harness = new AgentHarness(
            client,
            Registry(tool),
            new AgentHarnessOptions { Steering = steering }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Null(Assert.Single(events.OfType<SteeringInjected>()).EntryId);
    }

    [Fact]
    public async Task A_steering_session_recording_reproduces_byte_for_byte()
    {
        using var temp = new TempDirectory();
        string recordedPath = temp.File("recorded.jsonl");
        string replayPath = temp.File("replay.jsonl");

        await RecordSteeringRunAsync(recordedPath);
        await RecordSteeringRunAsync(replayPath);

        string[] recorded = File.ReadAllLines(recordedPath);
        string[] replay = File.ReadAllLines(replayPath);
        Assert.Equal(recorded.Length, replay.Length);
        Assert.Equal(recorded[1..], replay[1..]);
        Assert.Contains("\"parentId\":\"e_03\"", recorded[4], StringComparison.Ordinal);
        Assert.Contains("\"first steer\"", recorded[4], StringComparison.Ordinal);
    }

    private static async Task RecordSteeringRunAsync(string path)
    {
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        var steering = new SteeringQueue();
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call-1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var tool = ReadTool("contents");
        tool.OnExecute = (_, _) =>
        {
            steering.Enqueue("first steer");
            return new ToolResult("contents", IsError: false);
        };
        var harness = new AgentHarness(
            client,
            Registry(tool),
            new AgentHarnessOptions { Steering = steering, Session = session }
        );

        await Run(harness);
    }
}

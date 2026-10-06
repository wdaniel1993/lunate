using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AgentToolExecutionTests
{
    [Fact]
    public async Task One_tool_call_runs_between_its_events_and_the_run_finishes()
    {
        ScriptedTool tool = ReadTool("file contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(tool));

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        AssertOrder(
            events,
            new ToolCallStart(events[0].RunId, "call_1", "read"),
            new ToolCallArgs(events[0].RunId, "call_1", """{"path":"a.txt"}"""),
            new ToolCallEnd(events[0].RunId, "call_1"),
            new ToolCallResult(events[0].RunId, "call_1", "file contents", IsError: false)
        );
        Assert.Equal(StopReasons.Stop, Assert.IsType<RunFinished>(events[^1]).StopReason);

        Assert.Equal("""{"path":"a.txt"}""", tool.ReceivedArgsRaw);
        Assert.NotNull(tool.ReceivedContext);
        ChatMessage toolMessage = ToolMessage(client.Requests[1]);
        Assert.Equal("file contents", Result(toolMessage).Result);
        Assert.Contains(
            client.Requests[1].Messages,
            message =>
                message.Role == ChatRole.Assistant
                && message.Contents.OfType<FunctionCallContent>().Any()
        );
    }

    [Fact]
    public async Task Two_calls_in_one_step_execute_in_order()
    {
        ScriptedTool read = ReadTool("contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Calls(
                    new FunctionCallContent("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                    new FunctionCallContent("call_2", "read", LoopScripts.Args(("path", "b.txt")))
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(read));

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        Assert.Equal(
            ["call_1", "call_2"],
            events.OfType<ToolCallResult>().Select(result => result.CallId)
        );
        Assert.Equal(
            ["call_1", "call_2"],
            client
                .Requests[1]
                .Messages.Where(message => message.Role == ChatRole.Tool)
                .Select(message => Result(message).CallId)
        );
    }

    [Fact]
    public async Task A_tool_call_per_step_loops_until_the_final_answer()
    {
        ScriptedTool read = ReadTool("contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("call_2", "read", LoopScripts.Args(("path", "b.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(read));

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        Assert.Equal(3, client.Requests.Count);
        Assert.Equal(2, events.OfType<ToolCallResult>().Count());
        Assert.Equal(StopReasons.Stop, Assert.IsType<RunFinished>(events[^1]).StopReason);
    }

    [Fact]
    public async Task Long_tool_output_is_truncated_before_the_history()
    {
        string longOutput = new string('x', ToolOutput.DefaultLimit + 100);
        ScriptedTool tool = ReadTool(longOutput);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(tool));

        List<AgentEvent> events = await Run(harness);

        string expected = ToolOutput.Truncate(longOutput);
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.Equal(expected, result.Output);
        Assert.Contains("characters truncated", result.Output);
        Assert.Equal(expected, Result(ToolMessage(client.Requests[1])).Result);
    }

    [Fact]
    public async Task Details_are_ui_only_and_never_enter_the_history()
    {
        var details = new { Diff = "+ added line" };
        ScriptedTool tool = ReadTool("contents");
        tool.OnExecute = (_, _) => new ToolResult("edited", IsError: false, Details: details);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(tool));

        List<AgentEvent> events = await Run(harness);

        Assert.Same(details, Assert.Single(events.OfType<ToolCallResult>()).Details);
        FunctionResultContent functionResult = Result(ToolMessage(client.Requests[1]));
        Assert.Equal("edited", functionResult.Result);
        Assert.DoesNotContain("added line", functionResult.Result!.ToString());
    }

    [Fact]
    public async Task Tool_context_carries_the_working_directory_and_the_shared_event_sink()
    {
        const string workingDirectory = "/tmp/lunate-workspace";
        ScriptedTool tool = ReadTool("contents");
        tool.OnExecute = (_, context) =>
        {
            context.Events.Emit(
                new UsageUpdated("from_tool", new UsageDetails { InputTokenCount = 7 })
            );
            return new ToolResult("contents", IsError: false);
        };
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(tool),
            new AgentHarnessOptions { WorkingDirectory = workingDirectory }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Equal(workingDirectory, tool.ReceivedContext!.WorkingDirectory);
        Assert.IsType<ExtensionOnlyEventSink>(tool.ReceivedContext.Events);
        int callEnd = events.FindIndex(agentEvent => agentEvent is ToolCallEnd);
        int toolEvent = events.FindIndex(agentEvent => agentEvent is UsageUpdated);
        int toolResult = events.FindIndex(agentEvent => agentEvent is ToolCallResult);
        Assert.True(callEnd < toolEvent && toolEvent < toolResult);
    }

    private static ChatMessage ToolMessage(ScriptedRequest request) =>
        request.Messages.Single(message => message.Role == ChatRole.Tool);

    private static FunctionResultContent Result(ChatMessage toolMessage) =>
        Assert.IsType<FunctionResultContent>(Assert.Single(toolMessage.Contents));

    private static void AssertOrder(List<AgentEvent> events, params AgentEvent[] expected)
    {
        int previous = -1;
        foreach (AgentEvent agentEvent in expected)
        {
            int index = events.FindIndex(previous + 1, candidate => candidate.Equals(agentEvent));
            Assert.True(index >= 0, $"Event {agentEvent} was not found after index {previous}.");
            previous = index;
        }
    }
}

using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AgentLimitsTests
{
    [Fact]
    public async Task The_step_limit_ends_a_run_that_keeps_calling_tools()
    {
        ScriptedTool read = ReadTool("contents");
        var client = new ScriptedChatClient();
        for (int call = 1; call <= 3; call++)
        {
            client.Enqueue(
                LoopScripts.Call($"call_{call}", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            );
        }

        var harness = new AgentHarness(
            client,
            Registry(read),
            new AgentHarnessOptions { MaxSteps = 3 }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        Assert.Equal(3, client.Requests.Count);
        Assert.Equal(3, events.OfType<ToolCallResult>().Count());
        StepLimitReached limit = Assert.Single(events.OfType<StepLimitReached>());
        Assert.Equal(3, limit.MaxSteps);
        RunFinished finished = Assert.IsType<RunFinished>(events[^1]);
        Assert.Equal(StopReasons.StepLimit, finished.StopReason);
        int lastToolResult = events.FindLastIndex(agentEvent => agentEvent is ToolCallResult);
        int stepLimit = events.FindIndex(agentEvent => agentEvent is StepLimitReached);
        Assert.True(lastToolResult < stepLimit && stepLimit < events.Count - 1);
    }

    [Fact]
    public async Task An_answer_on_the_last_allowed_call_finishes_with_stop()
    {
        ScriptedTool read = ReadTool("contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read),
            new AgentHarnessOptions { MaxSteps = 2 }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        Assert.Equal(2, client.Requests.Count);
        Assert.Empty(events.OfType<StepLimitReached>());
        Assert.Equal(StopReasons.Stop, Assert.IsType<RunFinished>(events[^1]).StopReason);
    }
}

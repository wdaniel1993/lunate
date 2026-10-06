using System.Diagnostics;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AgentSpansTests
{
    [Fact]
    public async Task A_run_produces_one_run_span_with_nested_model_and_tool_spans()
    {
        using var listener = new RecordingActivityListener();
        ScriptedTool read = ReadTool("contents");
        var provider = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(TestPipeline.Create(provider), Registry(read));

        await Run(harness);

        Activity run = Assert.Single(
            listener.Activities,
            activity => activity.DisplayName == "invoke_agent lunate"
        );
        Assert.Equal("Lunate.Agent", run.Source.Name);
        Assert.Equal(ActivityKind.Internal, run.Kind);
        Assert.Equal("invoke_agent", run.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("lunate", run.GetTagItem("gen_ai.agent.name"));

        List<Activity> modelSpans =
        [
            .. listener.Activities.Where(activity =>
                activity.Source.Name.Contains("Microsoft.Extensions.AI", StringComparison.Ordinal)
            ),
        ];
        Assert.Equal(2, modelSpans.Count);
        Assert.All(modelSpans, span => Assert.Equal(run.SpanId, span.ParentSpanId));

        Activity tool = Assert.Single(
            listener.Activities,
            activity => activity.DisplayName == "execute_tool read"
        );
        Assert.Equal(run.SpanId, tool.ParentSpanId);
        Assert.Equal("execute_tool", tool.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("read", tool.GetTagItem("gen_ai.tool.name"));
        Assert.Equal("call_1", tool.GetTagItem("gen_ai.tool.call.id"));
        Assert.Equal(false, tool.GetTagItem("lunate.tool.is_error"));
    }

    [Fact]
    public async Task Tool_spans_flag_error_results()
    {
        using var listener = new RecordingActivityListener();
        ScriptedTool read = ReadTool("contents");
        read.OnExecute = (_, _) => throw new InvalidOperationException("disk exploded");
        var provider = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(TestPipeline.Create(provider), Registry(read));

        await Run(harness);

        Activity tool = Assert.Single(
            listener.Activities,
            activity => activity.DisplayName == "execute_tool read"
        );
        Assert.Equal(true, tool.GetTagItem("lunate.tool.is_error"));
    }

    [Fact]
    public async Task Without_a_listener_no_spans_are_produced()
    {
        using var listener = new RecordingActivityListener(shouldListen: _ => false);
        ScriptedTool read = ReadTool("contents");
        var provider = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(TestPipeline.Create(provider), Registry(read));

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(listener.Activities);
        Assert.Empty(EventSequenceValidator.Validate(events));
        Assert.Equal(StopReasons.Stop, Assert.IsType<RunFinished>(events[^1]).StopReason);
    }
}

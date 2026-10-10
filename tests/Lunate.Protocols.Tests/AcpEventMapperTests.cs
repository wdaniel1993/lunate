using Acp.Schema;
using Lunate.Agent;
using Lunate.Protocols.Acp;

namespace Lunate.Protocols.Tests;

/// <summary>Byte-exact wire payloads for every mapped event shape (design.md, mapper goldens).</summary>
public sealed class AcpEventMapperTests
{
    [Fact]
    public void Text_content_maps_to_an_agent_message_chunk()
    {
        Assert.Equal(
            """{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Hello"}}""",
            Wire(new TextMessageContent("run_1", "msg_1", "Hello"))
        );
    }

    [Fact]
    public void Tool_call_start_maps_to_a_tool_call_with_the_tool_title()
    {
        Assert.Equal(
            """{"sessionUpdate":"tool_call","toolCallId":"call_1","title":"echo","kind":"other","status":"in_progress"}""",
            Wire(new ToolCallStart("run_1", "call_1", "echo"))
        );
    }

    [Fact]
    public void Tool_call_args_map_to_a_raw_input_update()
    {
        Assert.Equal(
            """{"sessionUpdate":"tool_call_update","toolCallId":"call_1","rawInput":{"message":"hi"}}""",
            Wire(new ToolCallArgs("run_1", "call_1", """{"message":"hi"}"""))
        );
    }

    [Fact]
    public void Unparseable_tool_call_args_map_to_the_raw_string()
    {
        Assert.Equal(
            """{"sessionUpdate":"tool_call_update","toolCallId":"call_1","rawInput":"not json"}""",
            Wire(new ToolCallArgs("run_1", "call_1", "not json"))
        );
    }

    [Fact]
    public void Tool_call_end_maps_to_a_bare_update()
    {
        Assert.Equal(
            """{"sessionUpdate":"tool_call_update","toolCallId":"call_1"}""",
            Wire(new ToolCallEnd("run_1", "call_1"))
        );
    }

    [Fact]
    public void A_successful_result_maps_to_a_completed_update_with_the_output()
    {
        Assert.Equal(
            """{"sessionUpdate":"tool_call_update","toolCallId":"call_1","status":"completed","content":[{"type":"content","content":{"type":"text","text":"echo: hi"}}]}""",
            Wire(new ToolCallResult("run_1", "call_1", "echo: hi", IsError: false))
        );
    }

    [Fact]
    public void A_failed_result_maps_to_a_failed_update()
    {
        Assert.Equal(
            """{"sessionUpdate":"tool_call_update","toolCallId":"call_1","status":"failed","content":[{"type":"content","content":{"type":"text","text":"boom"}}]}""",
            Wire(new ToolCallResult("run_1", "call_1", "boom", IsError: true))
        );
    }

    [Fact]
    public void Run_lifecycle_and_unmapped_events_have_no_wire_payload()
    {
        AgentEvent[] unmapped =
        [
            new RunStarted("run_1"),
            new RunFinished("run_1", StopReasons.Stop),
            new RunError("run_1", "boom"),
            new TextMessageStart("run_1", "msg_1"),
            new TextMessageEnd("run_1", "msg_1"),
            new UsageUpdated("run_1", new Microsoft.Extensions.AI.UsageDetails()),
            new Retrying("run_1", 1, "boom"),
            new StepLimitReached("run_1", 5),
            new CompactionApplied("run_1", ["entry_1"], 10),
            new SteeringInjected("run_1", "entry_2"),
            new ApprovalRequested("run_1", "call_1", "echo", "{}"),
            new ToolProgressUpdate("run_1", "call_1", "working"),
        ];

        foreach (AgentEvent agentEvent in unmapped)
        {
            Assert.Null(AcpEventMapper.Map(agentEvent));
        }
    }

    [Fact]
    public void Stop_reasons_map_onto_the_acp_vocabulary()
    {
        Assert.Equal(StopReason.EndTurn, AcpEventMapper.MapStopReason(StopReasons.Stop));
        Assert.Equal(StopReason.Cancelled, AcpEventMapper.MapStopReason(StopReasons.Cancelled));
        Assert.Equal(StopReason.MaxTokens, AcpEventMapper.MapStopReason(StopReasons.Length));
        Assert.Equal(
            StopReason.MaxTurnRequests,
            AcpEventMapper.MapStopReason(StopReasons.StepLimit)
        );
        Assert.Equal(StopReason.EndTurn, AcpEventMapper.MapStopReason("unexpected"));
        Assert.Equal(StopReason.Refusal, AcpEventMapper.ErrorStopReason);
    }

    private static string Wire(AgentEvent agentEvent)
    {
        SessionUpdate? update = AcpEventMapper.Map(agentEvent);
        Assert.NotNull(update);
        return AcpTestSupport.Wire(update);
    }
}

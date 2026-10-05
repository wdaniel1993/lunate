using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class EventSequenceValidatorTests
{
    private const string RunId = "run_1";

    [Fact]
    public void A_well_formed_run_reports_no_violations()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new TextMessageStart(RunId, "msg_1"),
            new TextMessageContent(RunId, "msg_1", "hello "),
            new UsageUpdated(RunId, new UsageDetails()),
            new TextMessageContent(RunId, "msg_1", "world"),
            new TextMessageEnd(RunId, "msg_1"),
            new ToolCallStart(RunId, "call_1", "read"),
            new ToolCallArgs(RunId, "call_1", """{"path":"a.txt"}"""),
            new ToolCallEnd(RunId, "call_1"),
            new ToolCallResult(RunId, "call_1", "1  hello", IsError: false),
            new ApprovalRequested(RunId, "call_1", "read", """{"path":"a.txt"}"""),
            new ToolCallStart(RunId, "call_2", "bash"),
            new ToolCallEnd(RunId, "call_2"),
            new ToolCallResult(RunId, "call_2", "ok", IsError: false),
            new RunFinished(RunId, "end_turn"));

        Assert.Empty(EventSequenceValidator.Validate(sequence));
    }

    [Fact]
    public void A_run_ending_in_error_is_valid()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new RunError(RunId, "provider failed"));

        Assert.Empty(EventSequenceValidator.Validate(sequence));
    }

    [Fact]
    public void An_empty_sequence_reports_a_missing_start()
    {
        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(Sequence()));

        Assert.Equal(EventSequenceViolationKind.MissingRunStarted, violation.Kind);
        Assert.Null(violation.Event);
    }

    [Fact]
    public void A_sequence_that_does_not_begin_with_RunStarted_reports_a_missing_start()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new TextMessageStart(RunId, "msg_1"),
            new TextMessageEnd(RunId, "msg_1"),
            new RunFinished(RunId, "end_turn"));

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.MissingRunStarted, violation.Kind);
        Assert.Same(sequence[0], violation.Event);
    }

    [Fact]
    public void Text_content_outside_start_and_end_reports_an_unbracketed_message()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new TextMessageContent(RunId, "msg_1", "unbracketed"),
            new RunFinished(RunId, "end_turn"));

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.UnbracketedTextMessage, violation.Kind);
        Assert.Same(sequence[1], violation.Event);
    }

    [Fact]
    public void Text_content_after_the_message_ended_reports_an_unbracketed_message()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new TextMessageStart(RunId, "msg_1"),
            new TextMessageEnd(RunId, "msg_1"),
            new TextMessageContent(RunId, "msg_1", "late"),
            new RunFinished(RunId, "end_turn"));

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.UnbracketedTextMessage, violation.Kind);
        Assert.Same(sequence[3], violation.Event);
    }

    [Fact]
    public void A_tool_result_before_the_call_ended_reports_wrong_tool_order()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new ToolCallStart(RunId, "call_1", "read"),
            new ToolCallResult(RunId, "call_1", "1  hello", IsError: false),
            new ToolCallEnd(RunId, "call_1"),
            new RunFinished(RunId, "end_turn"));

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.ToolCallOutOfOrder, violation.Kind);
        Assert.Same(sequence[2], violation.Event);
    }

    [Fact]
    public void Tool_args_after_the_call_ended_reports_wrong_tool_order()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new ToolCallStart(RunId, "call_1", "read"),
            new ToolCallEnd(RunId, "call_1"),
            new ToolCallArgs(RunId, "call_1", """{"path":"a.txt"}"""),
            new ToolCallResult(RunId, "call_1", "1  hello", IsError: false),
            new RunFinished(RunId, "end_turn"));

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.ToolCallOutOfOrder, violation.Kind);
        Assert.Same(sequence[3], violation.Event);
    }

    [Fact]
    public void An_event_after_the_terminal_event_reports_the_offending_event()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new RunFinished(RunId, "end_turn"),
            new UsageUpdated(RunId, new UsageDetails()));

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.EventAfterTerminal, violation.Kind);
        Assert.Same(sequence[2], violation.Event);
    }

    [Fact]
    public void A_sequence_without_a_terminal_event_reports_a_missing_terminal()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new TextMessageStart(RunId, "msg_1"),
            new TextMessageEnd(RunId, "msg_1"));

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.MissingTerminalEvent, violation.Kind);
        Assert.Same(sequence[2], violation.Event);
    }

    private static IReadOnlyList<AgentEvent> Sequence(params AgentEvent[] events)
    {
        var recording = new RecordingAgentEvents();
        foreach (AgentEvent agentEvent in events)
        {
            recording.Emit(agentEvent);
        }

        return recording.Events;
    }
}

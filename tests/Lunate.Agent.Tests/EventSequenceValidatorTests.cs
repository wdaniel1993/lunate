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
            new ToolCallArgs(RunId, "call_2", """{"command":"echo ok"}"""),
            new ToolCallEnd(RunId, "call_2"),
            new ToolCallResult(RunId, "call_2", "ok", IsError: false),
            new RunFinished(RunId, StopReasons.Stop)
        );

        Assert.Empty(EventSequenceValidator.Validate(sequence));
    }

    [Fact]
    public void A_run_ending_in_error_is_valid()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new RunError(RunId, "provider failed")
        );

        Assert.Empty(EventSequenceValidator.Validate(sequence));
    }

    [Fact]
    public void An_empty_sequence_reports_a_missing_start()
    {
        EventSequenceViolation violation = Assert.Single(
            EventSequenceValidator.Validate(Sequence())
        );

        Assert.Equal(EventSequenceViolationKind.MissingRunStarted, violation.Kind);
        Assert.Null(violation.Event);
    }

    [Fact]
    public void A_sequence_that_does_not_begin_with_RunStarted_reports_a_missing_start()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new TextMessageStart(RunId, "msg_1"),
            new TextMessageEnd(RunId, "msg_1"),
            new RunFinished(RunId, StopReasons.Stop)
        );

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
            new RunFinished(RunId, StopReasons.Stop)
        );

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
            new RunFinished(RunId, StopReasons.Stop)
        );

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
            new ToolCallArgs(RunId, "call_1", """{"path":"a.txt"}"""),
            new ToolCallResult(RunId, "call_1", "1  hello", IsError: false),
            new ToolCallEnd(RunId, "call_1"),
            new RunFinished(RunId, StopReasons.Stop)
        );

        IReadOnlyList<EventSequenceViolation> violations = EventSequenceValidator.Validate(
            sequence
        );

        Assert.Collection(
            violations,
            violation =>
            {
                Assert.Equal(EventSequenceViolationKind.ToolCallOutOfOrder, violation.Kind);
                Assert.Same(sequence[3], violation.Event);
            },
            violation =>
            {
                Assert.Equal(EventSequenceViolationKind.IncompleteToolCall, violation.Kind);
                Assert.Same(sequence[^1], violation.Event);
            }
        );
    }

    [Fact]
    public void Tool_args_after_the_call_ended_reports_wrong_tool_order()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new ToolCallStart(RunId, "call_1", "read"),
            new ToolCallArgs(RunId, "call_1", """{"path":"a.txt"}"""),
            new ToolCallEnd(RunId, "call_1"),
            new ToolCallArgs(RunId, "call_1", """{"path":"b.txt"}"""),
            new ToolCallResult(RunId, "call_1", "1  hello", IsError: false),
            new RunFinished(RunId, StopReasons.Stop)
        );

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.ToolCallOutOfOrder, violation.Kind);
        Assert.Same(sequence[4], violation.Event);
    }

    [Fact]
    public void Tool_call_end_before_args_reports_wrong_tool_order()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new ToolCallStart(RunId, "call_1", "read"),
            new ToolCallEnd(RunId, "call_1"),
            new RunFinished(RunId, StopReasons.Stop)
        );

        IReadOnlyList<EventSequenceViolation> violations = EventSequenceValidator.Validate(
            sequence
        );

        Assert.Collection(
            violations,
            violation =>
            {
                Assert.Equal(EventSequenceViolationKind.ToolCallOutOfOrder, violation.Kind);
                Assert.Same(sequence[2], violation.Event);
            },
            violation =>
            {
                Assert.Equal(EventSequenceViolationKind.IncompleteToolCall, violation.Kind);
                Assert.Same(sequence[^1], violation.Event);
            }
        );
    }

    [Fact]
    public void A_second_RunStarted_reports_a_duplicate_start()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new RunStarted(RunId),
            new RunFinished(RunId, StopReasons.Stop)
        );

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.DuplicateRunStarted, violation.Kind);
        Assert.Same(sequence[1], violation.Event);
    }

    [Fact]
    public void Text_message_end_without_a_start_reports_an_unbracketed_message()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new TextMessageEnd(RunId, "msg_1"),
            new RunFinished(RunId, StopReasons.Stop)
        );

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.UnbracketedTextMessage, violation.Kind);
        Assert.Same(sequence[1], violation.Event);
    }

    [Fact]
    public void A_duplicate_TextMessageStart_reports_an_unbracketed_message()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new TextMessageStart(RunId, "msg_1"),
            new TextMessageStart(RunId, "msg_1"),
            new TextMessageEnd(RunId, "msg_1"),
            new RunFinished(RunId, StopReasons.Stop)
        );

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.UnbracketedTextMessage, violation.Kind);
        Assert.Same(sequence[2], violation.Event);
    }

    [Fact]
    public void A_second_terminal_event_and_what_follows_it_are_reported()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new RunFinished(RunId, StopReasons.Stop),
            new RunError(RunId, "provider failed"),
            new UsageUpdated(RunId, new UsageDetails())
        );

        IReadOnlyList<EventSequenceViolation> violations = EventSequenceValidator.Validate(
            sequence
        );

        Assert.Collection(
            violations,
            violation =>
            {
                Assert.Equal(EventSequenceViolationKind.EventAfterTerminal, violation.Kind);
                Assert.Same(sequence[2], violation.Event);
            },
            violation =>
            {
                Assert.Equal(EventSequenceViolationKind.EventAfterTerminal, violation.Kind);
                Assert.Same(sequence[3], violation.Event);
            }
        );
    }

    [Fact]
    public void An_event_after_the_terminal_event_reports_the_offending_event()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new RunFinished(RunId, StopReasons.Stop),
            new UsageUpdated(RunId, new UsageDetails())
        );

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
            new TextMessageEnd(RunId, "msg_1")
        );

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.MissingTerminalEvent, violation.Kind);
        Assert.Same(sequence[2], violation.Event);
    }

    [Fact]
    public void An_open_text_message_at_the_terminal_event_reports_an_unclosed_message()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new TextMessageStart(RunId, "msg_1"),
            new TextMessageContent(RunId, "msg_1", "cut off"),
            new RunFinished(RunId, StopReasons.Length)
        );

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.UnclosedTextMessage, violation.Kind);
        Assert.Same(sequence[^1], violation.Event);
    }

    [Fact]
    public void A_tool_call_without_a_result_at_the_terminal_event_reports_an_incomplete_call()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new ToolCallStart(RunId, "call_1", "read"),
            new ToolCallArgs(RunId, "call_1", """{"path":"a.txt"}"""),
            new ToolCallEnd(RunId, "call_1"),
            new RunFinished(RunId, StopReasons.Stop)
        );

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.IncompleteToolCall, violation.Kind);
        Assert.Same(sequence[^1], violation.Event);
    }

    [Fact]
    public void An_event_from_a_different_run_reports_a_foreign_run_id()
    {
        IReadOnlyList<AgentEvent> sequence = Sequence(
            new RunStarted(RunId),
            new UsageUpdated("run_2", new UsageDetails()),
            new RunFinished(RunId, StopReasons.Stop)
        );

        EventSequenceViolation violation = Assert.Single(EventSequenceValidator.Validate(sequence));

        Assert.Equal(EventSequenceViolationKind.ForeignRunId, violation.Kind);
        Assert.Same(sequence[1], violation.Event);
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

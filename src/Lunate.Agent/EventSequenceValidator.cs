namespace Lunate.Agent;

internal enum EventSequenceViolationKind
{
    MissingRunStarted,
    DuplicateRunStarted,
    MissingTerminalEvent,
    EventAfterTerminal,
    UnbracketedTextMessage,
    ToolCallOutOfOrder,
}

internal sealed record EventSequenceViolation(
    EventSequenceViolationKind Kind,
    AgentEvent? Event,
    string Message
);

/// <summary>
/// Checks a run's event stream against the sequence rules. It reports violations instead of throwing
/// so callers (loop tests, replay checks) decide severity.
/// </summary>
internal static class EventSequenceValidator
{
    public static IReadOnlyList<EventSequenceViolation> Validate(IEnumerable<AgentEvent> events)
    {
        AgentEvent[] sequence = [.. events];
        List<EventSequenceViolation> violations = [];
        if (sequence.Length == 0)
        {
            violations.Add(
                new(
                    EventSequenceViolationKind.MissingRunStarted,
                    null,
                    "A run must begin with RunStarted."
                )
            );
            return violations;
        }

        if (sequence[0] is not RunStarted)
        {
            violations.Add(
                new(
                    EventSequenceViolationKind.MissingRunStarted,
                    sequence[0],
                    "A run must begin with RunStarted."
                )
            );
        }

        bool runStartedSeen = false;
        bool terminalSeen = false;
        HashSet<string> openTextMessages = [];
        Dictionary<string, ToolCallState> toolCalls = [];

        foreach (AgentEvent agentEvent in sequence)
        {
            if (terminalSeen)
            {
                violations.Add(
                    new(
                        EventSequenceViolationKind.EventAfterTerminal,
                        agentEvent,
                        "No event may follow the terminal event."
                    )
                );
                continue;
            }

            switch (agentEvent)
            {
                case RunStarted start when runStartedSeen:
                    violations.Add(
                        new(
                            EventSequenceViolationKind.DuplicateRunStarted,
                            start,
                            "A run must have exactly one RunStarted."
                        )
                    );
                    break;
                case RunStarted:
                    runStartedSeen = true;
                    break;
                case RunFinished or RunError:
                    terminalSeen = true;
                    break;
                case TextMessageStart start when !openTextMessages.Add(start.MessageId):
                    violations.Add(
                        new(
                            EventSequenceViolationKind.UnbracketedTextMessage,
                            start,
                            $"TextMessageStart for message '{start.MessageId}' opened an already open message."
                        )
                    );
                    break;
                case TextMessageContent content when !openTextMessages.Contains(content.MessageId):
                    violations.Add(
                        new(
                            EventSequenceViolationKind.UnbracketedTextMessage,
                            content,
                            $"TextMessageContent for message '{content.MessageId}' is not bracketed by TextMessageStart/TextMessageEnd."
                        )
                    );
                    break;
                case TextMessageEnd end when !openTextMessages.Remove(end.MessageId):
                    violations.Add(
                        new(
                            EventSequenceViolationKind.UnbracketedTextMessage,
                            end,
                            $"TextMessageEnd for message '{end.MessageId}' has no matching TextMessageStart."
                        )
                    );
                    break;
                case ToolCallStart toolStart
                    when !toolCalls.TryAdd(toolStart.CallId, ToolCallState.Started):
                    violations.Add(
                        ToolCallViolation(
                            toolStart,
                            $"Tool call '{toolStart.CallId}' was started twice."
                        )
                    );
                    break;
                case ToolCallArgs args
                    when StateOf(toolCalls, args.CallId) != ToolCallState.Started:
                    violations.Add(ToolCallViolation(args, WrongToolOrder(args.CallId)));
                    break;
                case ToolCallEnd end when StateOf(toolCalls, end.CallId) != ToolCallState.ArgsSeen:
                    violations.Add(ToolCallViolation(end, WrongToolOrder(end.CallId)));
                    break;
                case ToolCallResult result
                    when StateOf(toolCalls, result.CallId) != ToolCallState.Ended:
                    violations.Add(ToolCallViolation(result, WrongToolOrder(result.CallId)));
                    break;
                case ToolCallArgs args:
                    toolCalls[args.CallId] = ToolCallState.ArgsSeen;
                    break;
                case ToolCallEnd end:
                    toolCalls[end.CallId] = ToolCallState.Ended;
                    break;
                case ToolCallResult result:
                    toolCalls[result.CallId] = ToolCallState.Completed;
                    break;
                default:
                    break;
            }
        }

        if (!terminalSeen)
        {
            violations.Add(
                new(
                    EventSequenceViolationKind.MissingTerminalEvent,
                    sequence[^1],
                    "A run must end with a terminal event (RunFinished or RunError)."
                )
            );
        }

        return violations;
    }

    private static string WrongToolOrder(string callId) =>
        $"Tool call '{callId}' must follow ToolCallStart -> ToolCallArgs -> ToolCallEnd -> ToolCallResult.";

    private static EventSequenceViolation ToolCallViolation(
        AgentEvent agentEvent,
        string message
    ) => new(EventSequenceViolationKind.ToolCallOutOfOrder, agentEvent, message);

    private static ToolCallState? StateOf(
        Dictionary<string, ToolCallState> toolCalls,
        string callId
    ) => toolCalls.TryGetValue(callId, out ToolCallState state) ? state : null;

    private enum ToolCallState
    {
        Started,
        ArgsSeen,
        Ended,
        Completed,
    }
}

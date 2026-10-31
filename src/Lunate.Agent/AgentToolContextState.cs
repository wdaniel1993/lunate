namespace Lunate.Agent;

internal sealed record AgentToolContextState(
    Session? Session,
    IReadLedger? Reads,
    Func<IReadOnlyList<UserQuestion>, CancellationToken, Task<UserQuestionResult>>? AskUserAsync
)
{
    internal static Session? SessionFor(ToolContext context) =>
        (context.AgentState as AgentToolContextState)?.Session;

    internal static IReadLedger? ReadsFor(ToolContext context) =>
        (context.AgentState as AgentToolContextState)?.Reads;
}

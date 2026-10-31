using Lunate.Contracts;

namespace Lunate.Agent;

internal sealed record AgentToolContextState(
    Session? Session,
    IReadLedger? Reads,
    Func<IReadOnlyList<UserQuestion>, CancellationToken, Task<UserQuestionResult>>? AskUserAsync
)
{
    internal Func<
        DecisionRequest,
        CancellationToken,
        Task<DecisionResponse>
    >? DecideAsync { get; init; }

    internal static Session? SessionFor(ToolContext context) =>
        (context.AgentState as AgentToolContextState)?.Session;

    internal static IReadLedger? ReadsFor(ToolContext context) =>
        (context.AgentState as AgentToolContextState)?.Reads;
}

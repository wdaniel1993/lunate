namespace Lunate.Agent;

internal sealed record AgentToolContextState(
    Func<IReadOnlyList<UserQuestion>, CancellationToken, Task<UserQuestionResult>>? AskUserAsync
);

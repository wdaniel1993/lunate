namespace Lunate.Agent;

/// <summary>
/// The resolved subagent limits of one run tree. A null value means unlimited (<c>unlimited</c> in
/// settings is only ever an explicit literal value); the defaults match the documented settings.
/// </summary>
/// <param name="Concurrency">The maximum number of live child runs per top-level run tree.</param>
/// <param name="TokenBudget">The cumulative input+output token budget of one child run.</param>
/// <param name="TreeTokenBudget">The cumulative input+output token budget of one top-level run tree.</param>
public sealed record SubagentLimits(
    int? Concurrency = 4,
    long? TokenBudget = 100_000,
    long? TreeTokenBudget = 500_000
);

/// <summary>The subagent accounting of one run tree: tokens consumed by its child runs and live children.</summary>
public readonly record struct SubagentUsage(long TokensUsed, int LiveChildren);

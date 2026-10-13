namespace Lunate.Agent;

/// <summary>
/// The per-run subagent seam stamped into every <see cref="ToolContext"/> the loop builds. A tool
/// starts a child run with <see cref="RunAsync"/>; the child runs on its own <see cref="AgentHarness"/>
/// composed from the parent's live facts and its events are forwarded onto the parent's stream with
/// <see cref="AgentEvent.ParentRunId"/> set.
/// </summary>
public interface ISubagentRunner
{
    /// <summary>The run's spawn depth: 0 for a top-level run, 1 for a subagent (fixed at 1 in this version).</summary>
    int Depth { get; }

    /// <summary>
    /// Runs a child agent on the request's prompt and returns its result. Refusals (depth,
    /// concurrency, token budget) are error results naming the limit and its setting key, never
    /// exceptions; the runner joins the child before returning.
    /// </summary>
    Task<SubagentResult> RunAsync(SubagentRequest request, CancellationToken ct);
}

/// <summary>
/// One spawn request. <see cref="MaxSteps"/> and <see cref="AllowedTools"/> may only tighten the
/// run's own limits: values above the resolved ceiling are clamped, never loosened.
/// </summary>
/// <param name="Prompt">The user input of the child run; must not be blank.</param>
/// <param name="MaxSteps">The child's step ceiling; null uses the parent's resolved ceiling.</param>
/// <param name="AllowedTools">
/// The model-facing names of the tools to offer the child — a subset of the run's own tools. An
/// unknown name refuses the spawn with a teaching error; null offers the parent's full set.
/// </param>
public sealed record SubagentRequest(
    string Prompt,
    int? MaxSteps = null,
    IReadOnlyList<string>? AllowedTools = null
);

/// <summary>The outcome of one child run: its session and run identity, output and accounting.</summary>
/// <param name="ChildSessionId">The child session's id; null when the parent had no session.</param>
/// <param name="RunId">The child run's id; null for a refusal, which never starts a run.</param>
/// <param name="Output">The child's final text (the partial text when the run was stopped by a limit).</param>
/// <param name="StopReason">The run vocabulary (<c>stop</c>, <c>cancelled</c>, ...) or a runner refusal.</param>
/// <param name="IsError">True for everything except <c>stop</c> and <c>length</c>.</param>
/// <param name="TokensUsed">The child's cumulative input+output tokens.</param>
public sealed record SubagentResult(
    string? ChildSessionId,
    string? RunId,
    string Output,
    string StopReason,
    bool IsError,
    long TokensUsed
);

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

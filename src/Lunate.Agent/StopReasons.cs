namespace Lunate.Agent;

/// <summary>
/// The vocabulary for <see cref="RunFinished.StopReason"/> and <see cref="SubagentResult.StopReason"/>.
/// The loop maps the final turn's model finish reason: <c>ChatFinishReason.Length</c> to
/// <see cref="Length"/>, everything else to <see cref="Stop"/>. Subagent results additionally use
/// the runner refusals (<see cref="DepthLimit"/>, <see cref="ConcurrencyLimit"/>,
/// <see cref="WorkspaceMissing"/>, <see cref="WorkspaceOutsideRepository"/>,
/// <see cref="WorkspaceToolsUnavailable"/>) and <see cref="TokenLimit"/>; <see cref="Error"/>
/// reports a child run that ended in <see cref="RunError"/> or a non-limit spawn refusal.
/// </summary>
public static class StopReasons
{
    /// <summary>The model finished the turn without further tool calls.</summary>
    public const string Stop = "stop";

    /// <summary>The run was cancelled through its cancellation token.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The model reached its token limit and the turn was cut off.</summary>
    public const string Length = "length";

    /// <summary>The run reached the step limit (MaxSteps).</summary>
    public const string StepLimit = "step_limit";

    /// <summary>The run stopped before a model call because its token budget was exhausted.</summary>
    public const string TokenLimit = "token_limit";

    /// <summary>A subagent spawn was refused because the spawn depth (fixed at 1) is reached.</summary>
    public const string DepthLimit = "depth_limit";

    /// <summary>A subagent spawn was refused because the tree-wide concurrency limit is held.</summary>
    public const string ConcurrencyLimit = "concurrency_limit";

    /// <summary>A subagent spawn named a workspace root that is missing or not a directory.</summary>
    public const string WorkspaceMissing = "workspace_missing";

    /// <summary>
    /// A subagent spawn named a workspace root that is not a worktree of the parent run's
    /// repository (a different git-common-dir).
    /// </summary>
    public const string WorkspaceOutsideRepository = "workspace_outside_repository";

    /// <summary>
    /// A subagent spawn named another workspace root but no workspace-tool factory is configured,
    /// so the child cannot be given tools built for that workspace.
    /// </summary>
    public const string WorkspaceToolsUnavailable = "workspace_tools_unavailable";

    /// <summary>A child run failed with a <see cref="RunError"/>, or a spawn was refused for a reason other than a limit.</summary>
    public const string Error = "error";
}

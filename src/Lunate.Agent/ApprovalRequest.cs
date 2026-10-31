using System.Text.Json;

namespace Lunate.Agent;

/// <summary>
/// One approval ask: the tool, its arguments as approved (after any ToolCalling hook mutation) and
/// the identity of the asking run. <see cref="Depth"/> is the asking run's spawn depth — 0 for a
/// top-level run, 1 for a subagent — and <see cref="ParentRunId"/> names the run that spawned it,
/// so a prompt can label a subagent as the asker. <see cref="WorkspaceRoot"/> is the asking run's
/// workspace root, so the prompt can also show where the call will act.
/// </summary>
/// <param name="Tool">The tool the run wants to execute.</param>
/// <param name="Args">The arguments as they will be executed.</param>
/// <param name="RunId">The asking run's id; null when the call runs outside the loop.</param>
/// <param name="ParentRunId">The asking run's parent run id; null for a top-level run.</param>
/// <param name="Depth">The asking run's spawn depth.</param>
/// <param name="WorkspaceRoot">The asking run's workspace root; null when unknown.</param>
public sealed record ApprovalRequest(
    ITool Tool,
    JsonElement Args,
    string? RunId,
    string? ParentRunId,
    int Depth,
    string? WorkspaceRoot = null
)
{
    /// <summary>Whether a decision recommendation escalated this call to a user prompt.</summary>
    public bool EscalationRequested { get; init; }

    /// <summary>The decision recommendation that caused escalation, when present.</summary>
    public string? DecisionRecommendation { get; init; }

    /// <summary>The probability assigned to the decision recommendation, when present.</summary>
    public double? DecisionProbability { get; init; }

    /// <summary>The resolved model id that made the decision, when present.</summary>
    public string? DecisionModelId { get; init; }

    /// <summary>The method used to make the decision: <c>native</c> or <c>adapter</c>.</summary>
    public string? DecisionMethod { get; init; }
}

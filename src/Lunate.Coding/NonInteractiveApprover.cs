using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// The print-mode approver: it maps the resolved <see cref="ApprovalPolicy"/> onto tool risk
/// without a prompt. <c>ask</c> allows read-only tools only; <c>auto-edit</c> also allows file
/// writes and edits; the per-run <c>--yolo</c> flag allows everything. The setting never carries
/// <c>yolo</c>. The interactive tracked-file refinement of the <c>ask</c> level lands with the
/// approval flow; this is the documented print-mode approximation.
/// </summary>
internal sealed class NonInteractiveApprover(ApprovalPolicy policy, bool yolo) : IToolApprover
{
    /// <summary>The resolved approval policy.</summary>
    public ApprovalPolicy Policy { get; } = policy;

    /// <summary>Whether the per-run <c>--yolo</c> flag allows every call.</summary>
    public bool Yolo { get; } = yolo;

    /// <summary>Returns true to execute the call, false to decline it.</summary>
    public ValueTask<bool> ApproveAsync(ITool tool, JsonElement args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return ValueTask.FromResult(Yolo || IsAllowed(Policy, tool.Risk));
    }

    /// <summary>The policy × risk matrix; an unknown policy denies everything.</summary>
    internal static bool IsAllowed(ApprovalPolicy policy, ToolRisk risk) =>
        policy switch
        {
            ApprovalPolicy.Ask => risk == ToolRisk.ReadOnly,
            ApprovalPolicy.AutoEdit => risk is ToolRisk.ReadOnly or ToolRisk.Write,
            _ => false,
        };
}

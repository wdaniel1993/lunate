using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// The print-mode approver: it maps the resolved <see cref="ApprovalPolicy"/> onto tool risk
/// without a prompt. <c>ask</c> allows read-only tools only; <c>auto-edit</c> also allows file
/// writes and edits; <c>Execute</c> is never allowed by a policy — there is no prompt in print
/// mode, so a command is declined rather than run silently. The per-run <c>--yolo</c> flag is the
/// explicit exception and allows everything; it is the flag that removes the panel, and the run
/// says so at startup. The setting never carries <c>yolo</c>. The interactive tracked-file
/// refinement of the <c>ask</c> level lands with the approval flow; this is the documented
/// print-mode approximation.
/// </summary>
internal sealed class NonInteractiveApprover(ApprovalPolicy policy, bool yolo) : IToolApprover
{
    /// <summary>The resolved approval policy.</summary>
    public ApprovalPolicy Policy { get; } = policy;

    /// <summary>Whether the per-run <c>--yolo</c> flag allows every call.</summary>
    public bool Yolo { get; } = yolo;

    /// <summary>Returns true to execute the call, false to decline it.</summary>
    public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        // A high-risk call is never waved through by a policy: with no panel to show it, print
        // mode declines it. Only the explicit --yolo flag (which the run announces) overrides.
        return ValueTask.FromResult(
            Yolo || (IsAllowed(Policy, request.Tool.Risk) && !HighRiskCall.Is(request))
        );
    }

    /// <summary>
    /// The policy × risk matrix; an unknown policy denies everything. <c>Execute</c> is excluded
    /// before the matrix, so no policy — present or future — can allow a command without a panel.
    /// </summary>
    internal static bool IsAllowed(ApprovalPolicy policy, ToolRisk risk) =>
        risk is not ToolRisk.Execute
        && policy switch
        {
            ApprovalPolicy.Ask => risk is ToolRisk.ReadOnly,
            ApprovalPolicy.AutoEdit => risk is ToolRisk.ReadOnly or ToolRisk.Write,
            _ => false,
        };
}

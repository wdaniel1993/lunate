using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// The ACP-mode approver: it composes the configured policy with the client's permission request.
/// Only a read-only call the policy already allows runs without asking; a write and a command
/// always reach the client's permission panel, whatever the policy level says — an editor's
/// request is never auto-approved. There is no yolo in ACP mode.
/// </summary>
internal sealed class AcpApprover(ApprovalPolicy policy, IToolApprover client)
    : IUserPromptToolApprover
{
    public ValueTask<bool> PromptAsync(ApprovalRequest request, CancellationToken ct) =>
        client is IUserPromptToolApprover promptApprover
            ? promptApprover.PromptAsync(request, ct)
            : ValueTask.FromResult(false);

    /// <summary>Returns true to execute the call, false to decline it.</summary>
    public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The policy can only widen what runs silently for observation. Anything that changes
        // files or runs a process is the editor's decision, so it asks even under auto-edit.
        return
            NonInteractiveApprover.IsAllowed(policy, request.Tool.Risk)
            && request.Tool.Risk is ToolRisk.ReadOnly
            ? ValueTask.FromResult(true)
            : client.ApproveAsync(request, ct);
    }
}

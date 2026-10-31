namespace Lunate.Agent;

/// <summary>
/// An approver that can ask a person to decide. The harness fails closed on an escalation when
/// the active approver does not implement this capability, and calls <see cref="PromptAsync"/>
/// directly rather than relying on an ordinary policy check to notice escalation metadata.
/// </summary>
public interface IUserPromptToolApprover : IToolApprover
{
    /// <summary>Always presents the ordinary approval prompt, without applying an allow envelope.</summary>
    ValueTask<bool> PromptAsync(ApprovalRequest request, CancellationToken ct);
}

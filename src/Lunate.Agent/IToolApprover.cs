namespace Lunate.Agent;

/// <summary>
/// The approval seam (the interactive prompt is T-21): the loop asks before executing a tool;
/// null on the options allows every call. A decline becomes an error result. The ask carries the
/// asking run's identity (<see cref="ApprovalRequest"/>), so an approver can label a subagent as
/// the asker; a session-wide "always" grant applies across the whole run tree.
/// </summary>
public interface IToolApprover
{
    /// <summary>Returns true to execute the call, false to decline it.</summary>
    ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct);
}

using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// The ACP-mode approver: it composes the configured policy with the client's permission request.
/// Calls the policy already allows (see <see cref="NonInteractiveApprover.IsAllowed"/>) run without
/// prompting; everything else — commands under every policy — asks the client. There is no yolo in
/// ACP mode.
/// </summary>
internal sealed class AcpApprover(ApprovalPolicy policy, IToolApprover client) : IToolApprover
{
    /// <summary>Returns true to execute the call, false to decline it.</summary>
    public ValueTask<bool> ApproveAsync(ITool tool, JsonElement args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return NonInteractiveApprover.IsAllowed(policy, tool.Risk)
            ? ValueTask.FromResult(true)
            : client.ApproveAsync(tool, args, ct);
    }
}

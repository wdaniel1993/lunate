using System.Text.Json;

namespace Lunate.Agent;

/// <summary>
/// The approval seam (the interactive prompt is T-21): the loop asks before executing a tool;
/// null on the options allows every call. A decline becomes an error result.
/// </summary>
public interface IToolApprover
{
    /// <summary>Returns true to execute the call, false to decline it.</summary>
    ValueTask<bool> ApproveAsync(ITool tool, JsonElement args, CancellationToken ct);
}

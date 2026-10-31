using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// Run-bound model access: <see cref="For"/> returns the client for a model id wired into the
/// owning run, so nested calls charge that run's usage. Stamped into <c>ToolContext.Models</c> by
/// the harness; null outside a run or without a registry.
/// </summary>
public interface IModelAccess
{
    /// <summary>
    /// The client for the model id, bound to the owning run. An unresolvable id throws the
    /// registry's teaching error.
    /// </summary>
    IChatClient For(string modelId);
}

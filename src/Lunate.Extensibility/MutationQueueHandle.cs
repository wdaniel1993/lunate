using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// Exposes the harness's per-path mutation queue to extensions as the reserved
/// <c>core/mutation-queue</c> handle: schedule a read-modify-write and it applies atomically with
/// the core's own file mutations.
/// </summary>
public sealed class MutationQueueHandle(IFileMutationQueue queue) : IMutationQueue
{
    public Task<T> RunAsync<T>(
        string path,
        Func<CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken
    ) => queue.RunAsync(path, mutation, cancellationToken);
}

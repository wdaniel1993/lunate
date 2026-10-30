namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// The factory behind one of an extension's per-workspace services (ADR-0017 addendum §(b)). The
/// extension registers it under the manifest <c>workspaceServices</c> entry's name; the host calls
/// it lazily on the service's first acquisition per worktree root.
/// </summary>
/// <typeparam name="TService">The per-workspace value type.</typeparam>
public interface IWorkspaceServiceFactory<TService>
    where TService : notnull
{
    /// <summary>
    /// Creates the value for one canonical worktree root. Runs outside any registry lock, and runs
    /// again after an entry's eviction disposed the previous value, so the value must be
    /// rebuildable from durable state. Creating must never sweep a shared store, delete durable
    /// data or reset a directory (ADR-0021: eviction disposes rebuildable resources only).
    /// </summary>
    TService Create(string canonicalWorktreeRoot);
}

/// <summary>
/// One root's lease on a loaded per-workspace value. The lease pins its entry for its lifetime: a
/// pinned entry is never evicted, and when every loaded entry is pinned a new acquisition waits
/// (cancellation-aware) until a lease is disposed. Disposing the lease releases the pin and wakes
/// waiters; it must not delete durable data.
/// </summary>
/// <typeparam name="TService">The per-workspace value type.</typeparam>
public interface IWorkspaceServiceLease<TService> : IDisposable
    where TService : notnull
{
    /// <summary>The root's value, created lazily before the lease was handed out.</summary>
    TService Value { get; }

    /// <summary>The canonical worktree root the lease is for.</summary>
    string WorktreeRoot { get; }
}

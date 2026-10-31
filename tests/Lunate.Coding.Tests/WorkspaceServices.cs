using System.Runtime.CompilerServices;
using Lunate.Agent;
using Lunate.Roslyn;

namespace Lunate.Coding;

/// <summary>
/// The session's per-workspace services, keyed by canonical <c>WorktreeRoot</c> (the registry is
/// shaped for the Roslyn backend and future LSP background services; ADR-0021). Values are created
/// lazily on first request and disposed on eviction or shutdown. At most
/// <see cref="MaxLoadedWorkspaces"/> backends may be loaded across roots: a further root's load
/// evicts the least-recently-used unpinned loaded backend. A backend with an in-flight call is
/// pinned and never evicted; when every loaded backend is pinned, a new root's load waits
/// (cancellation-aware) until a call finishes rather than unloading under a live call. Loads and
/// evictions are observable for tests through <see cref="LoadedWorkspaces"/> and
/// <see cref="Evictions"/>.
/// </summary>
internal sealed partial class WorkspaceServices : IDisposable
{
    /// <summary>The wait slice of the all-pinned wait; cancellation is checked between slices.</summary>
    private const int WaitSliceMilliseconds = 25;

    private readonly object _gate = new();
    private readonly Func<string, ICSharpBackend> _createBackend;
    private readonly Dictionary<string, Entry> _backends;
    private readonly LinkedList<string> _order = [];
    private readonly List<string> _evictions = [];
    private int _loaded;
    private bool _disposed;

    /// <summary>Creates the registry; <paramref name="createBackend"/> is the test seam.</summary>
    internal WorkspaceServices(
        int maxLoadedWorkspaces,
        Func<string, ICSharpBackend>? createBackend = null
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLoadedWorkspaces, 1);
        MaxLoadedWorkspaces = maxLoadedWorkspaces;
        _createBackend = createBackend ?? CreateRoslynBackend;
        _backends = new Dictionary<string, Entry>(RootComparison);
    }

    /// <summary>The loaded-workspace cap across roots.</summary>
    internal int MaxLoadedWorkspaces { get; }

    /// <summary>The canonical roots whose backends are currently loaded, loading included.</summary>
    internal IReadOnlyList<string> LoadedWorkspaces
    {
        get
        {
            lock (_gate)
            {
                return [.. _backends.Where(pair => pair.Value.Loaded).Select(pair => pair.Key)];
            }
        }
    }

    /// <summary>The roots evicted by the cap, in eviction order; observable for tests.</summary>
    internal IReadOnlyList<string> Evictions
    {
        get
        {
            lock (_gate)
            {
                return [.. _evictions];
            }
        }
    }

    /// <summary>
    /// The backend for a worktree root: a lightweight handle that leases the root's loaded backend
    /// for the duration of every call, so eviction can never unload under a call. Nothing loads
    /// before the handle's first <see cref="ICSharpBackend.LoadAsync"/>.
    /// </summary>
    internal ICSharpBackend BackendFor(string worktreeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeRoot);
        return new LeasedCSharpBackend(this, GitIdentity.Canonicalize(worktreeRoot));
    }

    /// <summary>Disposes every backend and the file-change subscription, and releases pinned
    /// waiters; later calls fail the lease.</summary>
    public void Dispose()
    {
        List<ICSharpBackend> backends;
        IDisposable? fileChanges;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            fileChanges = _fileChanges;
            _fileChanges = null;
            backends = [.. _backends.Values.Select(entry => entry.Backend)];
            _backends.Clear();
            _order.Clear();
            Monitor.PulseAll(_gate);
        }

        foreach (ICSharpBackend backend in backends)
        {
            DisposeBackend(backend);
        }

        fileChanges?.Dispose();
    }

    /// <summary>
    /// Leases the root's backend for one call. A load (the first call of a root) reserves a
    /// loaded-workspace slot first: the least-recently-used unpinned loaded backend is evicted when
    /// the cap is held, and an all-pinned set makes the load wait, cancellation-aware.
    /// </summary>
    private Lease Acquire(string root, bool needsLoad, CancellationToken ct)
    {
        while (true)
        {
            Entry entry = EnsureEntry(root);
            Lease? lease = null;
            ICSharpBackend? evicted = null;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!entry.Evicted)
                {
                    Touch(root);
                    if (!needsLoad || entry.Loaded || _loaded < MaxLoadedWorkspaces)
                    {
                        if (needsLoad && !entry.Loaded)
                        {
                            entry.Loaded = true;
                            _loaded++;
                        }

                        entry.Pins++;
                        lease = new Lease(this, entry);
                    }
                    else if (TryEvictLeastRecent(out evicted))
                    {
                        entry.Loaded = true;
                        _loaded++;
                        entry.Pins++;
                        lease = new Lease(this, entry);
                    }
                    else
                    {
                        // Every loaded backend is pinned: wait for a call to finish.
                        Monitor.Wait(_gate, WaitSliceMilliseconds);
                    }
                }
            }

            if (evicted is not null)
            {
                DisposeBackend(evicted);
            }

            if (lease is not null)
            {
                return lease;
            }

            ct.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// The root's entry, created on first request; a disposed registry refuses before constructing
    /// anything. Construction runs outside the gate and is double-checked on insert: a concurrent
    /// creator's backend, or one built while the registry disposed, is disposed outside the gate.
    /// </summary>
    private Entry EnsureEntry(string root)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_backends.TryGetValue(root, out Entry? existing))
            {
                return existing;
            }
        }

        ICSharpBackend created = _createBackend(root);
        Entry? duplicate = null;
        lock (_gate)
        {
            if (_backends.TryGetValue(root, out Entry? existing))
            {
                duplicate = existing;
            }
            else if (!_disposed)
            {
                var entry = new Entry(created);
                _backends[root] = entry;
                _order.AddFirst(root);
                return entry;
            }
        }

        // Not registered after all: a duplicate won the race, or the registry disposed while the
        // backend was being built. Dispose it outside the gate, then surface the failure.
        DisposeBackend(created);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return duplicate!;
    }

    /// <summary>Releases one pin and wakes waiters when the set becomes evictable again.</summary>
    private void Release(Entry entry)
    {
        lock (_gate)
        {
            entry.Pins--;
            Monitor.PulseAll(_gate);
        }
    }

    private void Touch(string root)
    {
        if (_order.First is { } first && first.Value == root)
        {
            return;
        }

        _order.Remove(root);
        _order.AddFirst(root);
    }

    /// <summary>
    /// Evicts the least-recently-used loaded backend that no call is using: removed from the
    /// registry, recorded and returned for disposal outside the gate. False when every loaded
    /// backend is pinned.
    /// </summary>
    private bool TryEvictLeastRecent(out ICSharpBackend? backend)
    {
        backend = null;
        for (LinkedListNode<string>? node = _order.Last; node is not null; node = node.Previous)
        {
            if (
                _backends.TryGetValue(node.Value, out Entry? candidate)
                && candidate.Loaded
                && candidate.Pins == 0
            )
            {
                _backends.Remove(node.Value);
                _order.Remove(node);
                candidate.Evicted = true;
                _loaded--;
                _evictions.Add(node.Value);
                backend = candidate.Backend;
                return true;
            }
        }

        return false;
    }

    private static StringComparer RootComparison =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private static void DisposeBackend(ICSharpBackend backend)
    {
        try
        {
            (backend as IDisposable)?.Dispose();
        }
        catch (Exception)
        {
            // A backend that fails to dispose must not take the registry down; its resources are
            // released by the GC path.
        }
    }

    /// <summary>
    /// The default backend factory, isolated so no Roslyn or MSBuild assembly loads before the
    /// first <c>cs_*</c> call (ADR-0006; the startup budget gate pins this).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ICSharpBackend CreateRoslynBackend(string root) => new RoslynBackend(root);

    private sealed class Entry(ICSharpBackend backend)
    {
        public ICSharpBackend Backend { get; } = backend;

        /// <summary>True once the backend's first call reserved a loaded-workspace slot.</summary>
        public bool Loaded { get; set; }

        /// <summary>The in-flight calls using the backend; a pinned entry is never evicted.</summary>
        public int Pins { get; set; }

        /// <summary>True once the cap evicted the entry; its backend must not be used again.</summary>
        public bool Evicted { get; set; }
    }

    private sealed class Lease(WorkspaceServices owner, Entry entry) : IDisposable
    {
        private bool _released;

        public ICSharpBackend Backend => entry.Backend;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            owner.Release(entry);
        }
    }

    /// <summary>
    /// The per-root backend handle: every call leases the root's backend for the call's duration
    /// (pinning it against eviction) and touches its recency. <see cref="LoadAsync"/> reserves the
    /// loaded-workspace slot and is the only call that can trigger an eviction.
    /// </summary>
    private sealed class LeasedCSharpBackend(WorkspaceServices services, string root)
        : ICSharpBackend
    {
        public async ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct)
        {
            using Lease lease = services.Acquire(root, needsLoad: true, ct);
            return await lease.Backend.LoadAsync(ct).ConfigureAwait(false);
        }

        public async ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
            DiagnosticsScope scope,
            CancellationToken ct
        )
        {
            using Lease lease = services.Acquire(root, needsLoad: false, ct);
            return await lease.Backend.GetDiagnosticsAsync(scope, ct).ConfigureAwait(false);
        }

        public async ValueTask<SymbolSearchResult> FindSymbolAsync(
            string name,
            CancellationToken ct
        )
        {
            using Lease lease = services.Acquire(root, needsLoad: false, ct);
            return await lease.Backend.FindSymbolAsync(name, ct).ConfigureAwait(false);
        }

        public async ValueTask<ReferencesResult> FindReferencesAsync(
            string name,
            CancellationToken ct
        )
        {
            using Lease lease = services.Acquire(root, needsLoad: false, ct);
            return await lease.Backend.FindReferencesAsync(name, ct).ConfigureAwait(false);
        }

        public async ValueTask<OutlineResult> OutlineAsync(string file, CancellationToken ct)
        {
            using Lease lease = services.Acquire(root, needsLoad: false, ct);
            return await lease.Backend.OutlineAsync(file, ct).ConfigureAwait(false);
        }

        public async ValueTask<RenamePlanResult> PlanRenameAsync(
            string name,
            string newName,
            CancellationToken ct
        )
        {
            using Lease lease = services.Acquire(root, needsLoad: false, ct);
            return await lease.Backend.PlanRenameAsync(name, newName, ct).ConfigureAwait(false);
        }

        public void NotifyFileChanged(string absolutePath) =>
            services.MarkChanged(root, absolutePath);

        public void NotifyProjectChanged() => services.MarkProjectChanged(root);
    }
}

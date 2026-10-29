using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// One (extension, service) pair's loaded per-workspace values, keyed by canonical worktree root —
/// the extension-facing generalization of the documented flagship lifecycle
/// (<c>src/Lunate.Coding/WorkspaceServices.cs</c>, ADR-0021): values are created lazily per root
/// outside the registry lock, a lease pins its entry for its lifetime, the cap evicts the
/// least-recently-used unpinned entry, all-pinned acquisitions wait in cancellation-aware slices
/// rather than unloading under a live call, disposal happens outside the lock, a shut-down registry
/// refuses further acquisitions with <see cref="ObjectDisposedException"/>, and a lease's disposal
/// wakes waiters. The flagship registry stays until #293 migrates Roslyn.
/// </summary>
internal sealed class WorkspaceServiceRegistry : IDisposable
{
    /// <summary>The wait slice of the all-pinned wait; cancellation is checked between slices.</summary>
    private const int WaitSliceMilliseconds = 25;

    private readonly object _gate = new();
    private readonly Func<string, object> _createValue;
    private readonly Dictionary<string, Entry> _values;
    private readonly LinkedList<string> _order = [];
    private readonly List<string> _evictions = [];
    private int _loaded;
    private bool _disposed;

    /// <summary>Creates the registry; <paramref name="createValue"/> is the extension's factory.</summary>
    internal WorkspaceServiceRegistry(int maxLoadedWorkspaces, Func<string, object> createValue)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLoadedWorkspaces, 1);
        MaxLoadedWorkspaces = maxLoadedWorkspaces;
        _createValue = createValue;
        _values = new Dictionary<string, Entry>(RootComparison);
    }

    /// <summary>The loaded-value cap across roots (the effective per-(extension, service) cap).</summary>
    internal int MaxLoadedWorkspaces { get; }

    /// <summary>The canonical roots whose values are currently loaded; observable for tests.</summary>
    internal IReadOnlyList<string> LoadedRoots
    {
        get
        {
            lock (_gate)
            {
                return [.. _values.Where(pair => pair.Value.Loaded).Select(pair => pair.Key)];
            }
        }
    }

    /// <summary>The canonical roots evicted by the cap, in eviction order; observable for tests.</summary>
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
    /// Acquires the root's value, pinning it for the lease's lifetime: the acquisition reserves a
    /// loaded slot first, so the least-recently-used unpinned value is evicted when the cap is held
    /// and an all-pinned set makes the acquisition wait, cancellation-aware.
    /// </summary>
    internal WorkspaceServiceLease Acquire(string worktreeRoot, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeRoot);
        string root = GitIdentity.Canonicalize(worktreeRoot);
        while (true)
        {
            Entry entry = EnsureEntry(root);
            WorkspaceServiceLease? lease = null;
            object? evicted = null;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!entry.Evicted)
                {
                    Touch(root);
                    if (
                        entry.Loaded
                        || _loaded < MaxLoadedWorkspaces
                        || TryEvictLeastRecent(out evicted)
                    )
                    {
                        if (!entry.Loaded)
                        {
                            entry.Loaded = true;
                            _loaded++;
                        }

                        entry.Pins++;
                        lease = new WorkspaceServiceLease(this, entry);
                    }
                    else
                    {
                        // Every loaded value is pinned: wait for a lease to be disposed.
                        Monitor.Wait(_gate, WaitSliceMilliseconds);
                    }
                }
            }

            if (evicted is not null)
            {
                DisposeValue(evicted);
            }

            if (lease is not null)
            {
                return lease;
            }

            ct.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Disposes every value, pinned included, and releases waiters; later acquisitions fail.</summary>
    public void Dispose()
    {
        List<object> values;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            values = [.. _values.Select(pair => pair.Value.Value)];
            _values.Clear();
            _order.Clear();
            Monitor.PulseAll(_gate);
        }

        foreach (object value in values)
        {
            DisposeValue(value);
        }
    }

    /// <summary>
    /// The root's entry, created on first acquisition; a disposed registry refuses before creating
    /// anything. Creation runs outside the gate and is double-checked on insert: a value built by a
    /// concurrent creator, or while the registry disposed, is disposed outside the gate.
    /// </summary>
    private Entry EnsureEntry(string root)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_values.TryGetValue(root, out Entry? existing))
            {
                return existing;
            }
        }

        object created = _createValue(root);
        Entry? duplicate = null;
        lock (_gate)
        {
            if (_values.TryGetValue(root, out Entry? existing))
            {
                duplicate = existing;
            }
            else if (!_disposed)
            {
                var entry = new Entry(root, created);
                _values[root] = entry;
                _order.AddFirst(root);
                return entry;
            }
        }

        // Not registered after all: a duplicate won the race, or the registry disposed while the
        // value was being built. Dispose it outside the gate, then surface the failure.
        DisposeValue(created);
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
    /// Evicts the least-recently-used loaded entry no lease is using: removed from the registry,
    /// recorded and returned for disposal outside the gate. False when every loaded entry is pinned.
    /// </summary>
    private bool TryEvictLeastRecent(out object? value)
    {
        value = null;
        for (LinkedListNode<string>? node = _order.Last; node is not null; node = node.Previous)
        {
            if (
                _values.TryGetValue(node.Value, out Entry? candidate)
                && candidate.Loaded
                && candidate.Pins == 0
            )
            {
                _values.Remove(node.Value);
                _order.Remove(node);
                candidate.Loaded = false;
                candidate.Evicted = true;
                _loaded--;
                _evictions.Add(node.Value);
                value = candidate.Value;
                return true;
            }
        }

        return false;
    }

    private static StringComparer RootComparison =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Disposal only: rebuildable resources are released, and the registry itself never deletes,
    /// sweeps or resets anything — that boundary is the service value's, not the registry's
    /// (ADR-0021's eviction rule).
    /// </summary>
    private static void DisposeValue(object value)
    {
        try
        {
            (value as IDisposable)?.Dispose();
        }
        catch (Exception)
        {
            // A value that fails to dispose must not take the registry down; its resources are
            // released by the GC path.
        }
    }

    internal sealed class Entry(string root, object value)
    {
        /// <summary>The canonical root the entry is keyed by.</summary>
        public string Root { get; } = root;

        public object Value { get; } = value;

        /// <summary>True once the first acquisition reserved a loaded slot.</summary>
        public bool Loaded { get; set; }

        /// <summary>The live leases on the entry; a pinned entry is never evicted.</summary>
        public int Pins { get; set; }

        /// <summary>True once the cap evicted the entry; a stale lease must re-loop on it.</summary>
        public bool Evicted { get; set; }
    }

    /// <summary>
    /// One root's lease: exposes the loaded value, is the pin against eviction, and its disposal
    /// releases the pin and wakes the all-pinned waiters.
    /// </summary>
    internal sealed class WorkspaceServiceLease(WorkspaceServiceRegistry owner, Entry entry)
        : IDisposable
    {
        private bool _released;

        public object Value => entry.Value;

        public string WorktreeRoot => entry.Root;

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
}

using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The workspace services the loaded extensions registered, in registration order, keyed by
/// (extension id, manifest name) — the per-workspace sibling of <see cref="ExtensionToolRegistry"/>
/// (ADR-0017 addendum §(b)). Registration connects the extension's factory with the manifest
/// declaration; each pair's cap is min(declared, host limit), so the manifest cannot raise it past
/// the host's own budget. Unregistering an extension (an unload or a failed load's rollback) or
/// session end disposes every loaded value and releases the pair's waiters.
/// </summary>
internal sealed class ExtensionWorkspaceServiceRegistry
{
    /// <summary>The cap a manifest entry that omits maxLoadedWorkspaces gets.</summary>
    private const int DefaultCap = 2;

    private readonly object _gate = new();
    private readonly List<Service> _services = [];
    private readonly int _hostCapLimit;

    /// <param name="hostCapLimit">The host's ceiling over every pair's cap; the manifest cannot raise it.</param>
    internal ExtensionWorkspaceServiceRegistry(int? hostCapLimit) =>
        _hostCapLimit = hostCapLimit ?? int.MaxValue;

    /// <summary>
    /// Registers one per-(extension, service) pair: refuses a duplicate name, and builds the pair's
    /// registry with the effective cap. <paramref name="declared"/> is the manifest entry backing
    /// the name, or null while the load is still pending its cross-check refusal.
    /// </summary>
    internal void Register(
        string extensionId,
        string name,
        Type serviceType,
        Func<string, object> createValue,
        ExtensionWorkspaceService? declared
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(createValue);
        lock (_gate)
        {
            if (_services.Any(service => service.Is(extensionId, name)))
            {
                throw new InvalidOperationException(
                    $"extension '{extensionId}': workspace service '{name}' was refused: the name is already registered."
                );
            }

            int cap = Math.Min(declared?.MaxLoadedWorkspaces ?? DefaultCap, _hostCapLimit);
            _services.Add(new Service(extensionId, name, serviceType, createValue, cap));
        }
    }

    /// <summary>
    /// Acquires the pair's value for one canonical worktree root, through its lifecycle registry.
    /// An unknown name and an acquire whose service type does not match the registration's are
    /// refused with a teaching message — a handler reads both.
    /// </summary>
    internal WorkspaceServiceRegistry.WorkspaceServiceLease Acquire(
        string extensionId,
        string name,
        Type serviceType,
        string worktreeRoot,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            Service? service = _services.FirstOrDefault(entry => entry.Is(extensionId, name));
            if (service is null)
            {
                throw new InvalidOperationException(
                    $"extension '{extensionId}': workspace service '{name}' is not registered; access it through the extension that registered it."
                );
            }
            if (!Type.Equals(service.ServiceType, serviceType))
            {
                throw new InvalidOperationException(
                    $"extension '{extensionId}': workspace service '{name}' produces '{service.ServiceType.Name}'; acquire it as that type, not as '{serviceType.Name}'."
                );
            }

            return service.Registry.Acquire(worktreeRoot, ct);
        }
    }

    /// <summary>The canonical roots whose values are loaded for one pair; observable for tests.</summary>
    internal IReadOnlyList<string> LoadedRoots(string extensionId, string name)
    {
        lock (_gate)
        {
            return ServiceBy(extensionId, name).Registry.LoadedRoots;
        }
    }

    /// <summary>The canonical roots the cap evicted for one pair; observable for tests.</summary>
    internal IReadOnlyList<string> Evictions(string extensionId, string name)
    {
        lock (_gate)
        {
            return ServiceBy(extensionId, name).Registry.Evictions;
        }
    }

    /// <summary>
    /// Removes every pair the extension registered and disposes its loaded values, pinned included;
    /// later acquisitions of the extension's names are refused (the context is marked disposed too).
    /// </summary>
    internal void Unregister(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        List<WorkspaceServiceRegistry> registries;
        lock (_gate)
        {
            registries =
            [
                .. _services
                    .Where(service => service.ExtensionId == extensionId)
                    .Select(service => service.Registry),
            ];
            _services.RemoveAll(service => service.ExtensionId == extensionId);
        }

        foreach (WorkspaceServiceRegistry registry in registries)
        {
            registry.Dispose();
        }
    }

    /// <summary>Session end: every pair's registry disposes every loaded value and releases waiters.</summary>
    internal void EndSession()
    {
        List<WorkspaceServiceRegistry> registries;
        lock (_gate)
        {
            registries = [.. _services.Select(service => service.Registry)];
            _services.Clear();
        }

        foreach (WorkspaceServiceRegistry registry in registries)
        {
            registry.Dispose();
        }
    }

    private Service ServiceBy(string extensionId, string name)
    {
        lock (_gate)
        {
            return _services.FirstOrDefault(entry => entry.Is(extensionId, name))
                ?? throw new InvalidOperationException(
                    $"extension '{extensionId}': workspace service '{name}' is not registered; access it through the extension that registered it."
                );
        }
    }

    /// <summary>The names one extension registered, in registration order (the cross-check reads it).</summary>
    internal IReadOnlyList<string> RegisteredServices(string extensionId)
    {
        lock (_gate)
        {
            return
            [
                .. _services
                    .Where(service => service.ExtensionId == extensionId)
                    .Select(service => service.Name),
            ];
        }
    }

    /// <summary>One registered (extension, service) pair and its lifecycle registry.</summary>
    private sealed class Service(
        string extensionId,
        string name,
        Type serviceType,
        Func<string, object> createValue,
        int cap
    )
    {
        public string ExtensionId { get; } = extensionId;

        public string Name { get; } = name;

        public Type ServiceType { get; } = serviceType;

        public WorkspaceServiceRegistry Registry { get; } = new(cap, createValue);

        public bool Is(string extensionId, string name) =>
            ExtensionId == extensionId && Name == name;
    }
}

using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The named service registry: extensions register services under lowercase names (optionally
/// prefixed <c>ext/&lt;extension-id&gt;/</c>), look each other up by name, and the host registers
/// reserved <c>core/</c> handles. Duplicate names across extensions and attempts to shadow core
/// services are refused.
/// </summary>
public sealed class ServiceRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Registration> _services = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _coreServices = new(StringComparer.Ordinal);

    /// <summary>Registers a service on behalf of the extension.</summary>
    public void Register(string extensionId, string name, IBackgroundService service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(service);
        ValidateName(name, extensionId);

        lock (_gate)
        {
            if (_services.TryGetValue(name, out Registration? existing))
            {
                throw new InvalidOperationException(
                    $"service name '{name}' is already registered by extension '{existing.ExtensionId}'; extension '{extensionId}' cannot register it again."
                );
            }

            _services[name] = new Registration(extensionId, service);
        }
    }

    /// <summary>Registers a core handle under a reserved <c>core/</c> name; hosts only.</summary>
    public void RegisterCore(string name, object handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handle);
        if (!name.StartsWith("core/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"core service names must start with 'core/'; '{name}' does not.",
                nameof(name)
            );
        }

        lock (_gate)
        {
            if (_coreServices.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"core service '{name}' is already registered."
                );
            }

            _coreServices[name] = handle;
        }
    }

    /// <summary>Looks up a service another extension registered.</summary>
    public bool TryGet(string name, out IBackgroundService? service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            if (_services.TryGetValue(name, out Registration? registration))
            {
                service = registration.Service;
                return true;
            }
        }

        service = null;
        return false;
    }

    /// <summary>Looks up a core handle by its reserved name.</summary>
    public bool TryGetCore<T>(string name, out T? handle)
        where T : class
    {
        handle = FindCore(name) as T;
        return handle is not null;
    }

    /// <summary>Returns the core handle under the name, or null when there is none.</summary>
    public object? FindCore(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            return _coreServices.GetValueOrDefault(name);
        }
    }

    /// <summary>Removes every service the extension registered.</summary>
    public void Unregister(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        lock (_gate)
        {
            foreach (
                string name in _services
                    .Where(pair =>
                        string.Equals(
                            pair.Value.ExtensionId,
                            extensionId,
                            StringComparison.Ordinal
                        )
                    )
                    .Select(pair => pair.Key)
                    .ToList()
            )
            {
                _services.Remove(name);
            }
        }
    }

    private static void ValidateName(string name, string extensionId)
    {
        if (name.StartsWith("core/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"service name '{name}' is reserved for core; extension '{extensionId}' cannot register it."
            );
        }

        string[] segments = name.Split('/');
        bool shape = segments.Length == 1 || (segments.Length == 3 && segments[0] == "ext");
        if (!shape || !segments.All(IsLowerSegment))
        {
            throw new ArgumentException(
                $"service name '{name}' must be lowercase [a-z0-9-]+ segments, optionally prefixed 'ext/<extension-id>/'; extension '{extensionId}' cannot register it.",
                nameof(name)
            );
        }
    }

    private static bool IsLowerSegment(string segment) =>
        segment.Length > 0
        && segment.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'
        );

    private sealed record Registration(string ExtensionId, IBackgroundService Service);
}

using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// Hosts the extensions' background services. <see cref="StartAsync"/> follows the
/// <c>SessionStarted</c> semantics: safe to run more than once, already-started services are not
/// restarted. A service that fails to start is reported with its extension id and name, its
/// extension's remaining unstarted services do not start, and its already-started services are
/// stopped best-effort. <see cref="StopAsync"/> is idempotent; failures are reported and never
/// thrown. <see cref="DropAsync"/> stops and forgets one extension's services.
/// </summary>
public sealed class BackgroundServiceHost
{
    private readonly IExtensionLog _log;
    private readonly object _gate = new();
    private readonly List<ExtensionServices> _extensions = [];
    private readonly HashSet<string> _failedExtensions = new(StringComparer.Ordinal);

    public BackgroundServiceHost(IExtensionLog? log = null)
    {
        _log = log ?? NullExtensionLog.Instance;
        Services = new ServiceRegistry();
    }

    /// <summary>Named service lookup shared with extensions; core handles live here too.</summary>
    public ServiceRegistry Services { get; }

    public void Register(string extensionId, string name, IBackgroundService service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(service);
        Services.Register(extensionId, name, service);
        lock (_gate)
        {
            ExtensionServices entry = GetOrAdd(extensionId);
            entry.Services.Add(new ServiceState(name, service));
        }
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ExtensionServices[] extensions;
        lock (_gate)
        {
            extensions = [.. _extensions];
        }

        foreach (ExtensionServices extension in extensions)
        {
            lock (_gate)
            {
                if (_failedExtensions.Contains(extension.ExtensionId))
                {
                    continue;
                }
            }

            await StartExtensionAsync(extension, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ExtensionServices[] extensions;
        lock (_gate)
        {
            extensions = [.. _extensions];
        }

        foreach (ExtensionServices extension in extensions)
        {
            foreach (ServiceState state in Started(extension).Reverse())
            {
                await StopServiceAsync(extension, state, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DropAsync(
        string extensionId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ExtensionServices? entry;
        lock (_gate)
        {
            entry = _extensions.FirstOrDefault(candidate =>
                string.Equals(candidate.ExtensionId, extensionId, StringComparison.Ordinal)
            );
            if (entry is not null)
            {
                _extensions.Remove(entry);
            }

            _failedExtensions.Remove(extensionId);
        }

        Services.Unregister(extensionId);
        if (entry is null)
        {
            return;
        }

        foreach (ServiceState state in Started(entry).Reverse())
        {
            await StopServiceAsync(entry, state, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask StartExtensionAsync(
        ExtensionServices extension,
        CancellationToken cancellationToken
    )
    {
        foreach (ServiceState state in Snapshot(extension))
        {
            if (state.Started)
            {
                continue;
            }

            try
            {
                await state.Service.StartAsync(cancellationToken).ConfigureAwait(false);
                state.Started = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _log.Error(
                    $"extension '{extension.ExtensionId}': background service '{state.Name}' failed to start: {exception.GetType().FullName}: {exception.Message}; its remaining services will not start."
                );
                lock (_gate)
                {
                    _failedExtensions.Add(extension.ExtensionId);
                }

                foreach (ServiceState started in Started(extension).Reverse())
                {
                    await StopServiceAsync(extension, started, cancellationToken)
                        .ConfigureAwait(false);
                }

                return;
            }
        }
    }

    private async ValueTask StopServiceAsync(
        ExtensionServices extension,
        ServiceState state,
        CancellationToken cancellationToken
    )
    {
        state.Started = false;
        try
        {
            await state.Service.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _log.Error(
                $"extension '{extension.ExtensionId}': background service '{state.Name}' failed to stop: {exception.GetType().FullName}: {exception.Message}."
            );
        }
    }

    private ExtensionServices GetOrAdd(string extensionId)
    {
        ExtensionServices? entry = _extensions.FirstOrDefault(candidate =>
            string.Equals(candidate.ExtensionId, extensionId, StringComparison.Ordinal)
        );
        if (entry is null)
        {
            entry = new ExtensionServices(extensionId);
            _extensions.Add(entry);
        }

        return entry;
    }

    private ServiceState[] Snapshot(ExtensionServices extension)
    {
        lock (_gate)
        {
            return [.. extension.Services];
        }
    }

    private ServiceState[] Started(ExtensionServices extension)
    {
        lock (_gate)
        {
            return [.. extension.Services.Where(state => state.Started)];
        }
    }

    private sealed class ExtensionServices(string extensionId)
    {
        public string ExtensionId { get; } = extensionId;

        public List<ServiceState> Services { get; } = [];
    }

    private sealed class ServiceState(string name, IBackgroundService service)
    {
        public string Name { get; } = name;

        public IBackgroundService Service { get; } = service;

        public bool Started { get; set; }
    }
}

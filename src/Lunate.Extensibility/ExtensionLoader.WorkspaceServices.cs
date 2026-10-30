using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The loader's workspace-service wiring: the per-extension context registers through the loader so
/// the manifest backs every name with its cap, and the manifest cross-check keeps the declaration
/// rule symmetric with tools (design D6): a registered-but-undeclared service refuses the load, a
/// declared-but-unregistered one is logged and dropped.
/// </summary>
public sealed partial class ExtensionLoader
{
    /// <summary>
    /// The one registration path an extension context reaches: the manifest declaration under the
    /// name carries the cap, so a pair is born with min(declared, host limit) already applied.
    /// </summary>
    private void RegisterWorkspaceService(
        ExtensionDescriptor descriptor,
        string name,
        Type serviceType,
        Func<string, object> createValue
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(createValue);
        ExtensionWorkspaceService? declared = descriptor.Manifest.WorkspaceServices.FirstOrDefault(
            entry => string.Equals(entry.Name, name, StringComparison.Ordinal)
        );
        _workspaceServices.Register(descriptor.Id, name, serviceType, createValue, declared);
    }

    /// <summary>
    /// The asymmetric manifest rule for workspace services, extending the tool rule (design D6):
    /// the manifest is what the user trusted before any code loaded, so every registered service
    /// must be declared — a registered-but-undeclared service refuses the load. A
    /// declared-but-unregistered name is logged as a warning and dropped (it was never registered,
    /// so nothing can be acquired under it).
    /// </summary>
    private void CrossCheckManifestWorkspaceServices(ExtensionDescriptor descriptor)
    {
        IReadOnlyList<string> registered = _workspaceServices.RegisteredServices(descriptor.Id);
        HashSet<string> declared = new(
            descriptor.Manifest.WorkspaceServices.Select(entry => entry.Name),
            StringComparer.Ordinal
        );
        foreach (string name in registered)
        {
            if (!declared.Contains(name))
            {
                throw new ExtensionLoadException(
                    $"extension '{descriptor.Id}': workspace service '{name}' is registered by code but not declared in the manifest 'workspaceServices' list; the manifest must cover everything the code contributes. Declare it or remove the registration."
                );
            }
        }

        foreach (ExtensionWorkspaceService entry in descriptor.Manifest.WorkspaceServices)
        {
            if (!registered.Contains(entry.Name, StringComparer.Ordinal))
            {
                (_options.Log ?? NullExtensionLog.Instance).Warn(
                    $"extension '{descriptor.Id}': manifest declares workspace service '{entry.Name}' but no registration matched; the entry is dropped."
                );
            }
        }
    }
}

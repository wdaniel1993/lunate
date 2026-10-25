using System.Reflection;
using System.Runtime.Loader;

namespace Lunate.Extensibility;

/// <summary>
/// The collectible load context of one packaged extension. The contract assemblies are shared with
/// the default context so contract types keep one identity across the boundary — including
/// <c>Lunate.Agent</c>, whose <c>ToolContext</c>/<c>ToolResult</c> the contract references; every
/// other dependency is isolated to the extension's own directory. A copy of a host-owned assembly
/// lying in that directory is not loaded from there, and <see cref="ShadowedNames"/> names it so the
/// load can say so once.
/// </summary>
public sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly string _directory;

    public ExtensionLoadContext(string extensionId, string directory)
        : base($"extension:{extensionId}", isCollectible: true)
    {
        _directory = directory;
        ShadowedNames = HostOwnedAssemblies.InDirectory(directory);
    }

    /// <summary>The host-owned assemblies the extension ships beside its entry assembly, if any.</summary>
    public IReadOnlyList<string> ShadowedNames { get; }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (HostOwnedAssemblies.IsHostOwned(assemblyName.Name))
        {
            return null;
        }

        string candidate = Path.Combine(_directory, assemblyName.Name + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }
}

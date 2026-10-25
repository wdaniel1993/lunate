using System.Reflection;
using System.Runtime.Loader;

namespace Lunate.Extensibility;

/// <summary>
/// The collectible load context of one single-file build. The host owns the shared contract
/// assemblies — a package that ships its own copy is accepted, reported once, and ignored — and
/// every other dependency binds to the build's own directory or to the exact asset the build's
/// manifest recorded for a locked package, so a load never re-resolves a graph and never picks a
/// file the compile did not see. <see cref="ShadowedPackages"/> names who shipped the copies the
/// context dropped.
/// </summary>
public sealed class SingleFileExtensionLoadContext : AssemblyLoadContext
{
    private readonly string _buildDirectory;
    private readonly Dictionary<string, string> _assemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _native = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _shadowedPackages = [];
    private readonly HashSet<string> _shadowedNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the probe map from <paramref name="build"/>'s manifest. A host-owned name found in a
    /// package's runtime or native assets is recorded as shadowed and never mapped: the host's copy
    /// is the one a load binds to.
    /// </summary>
    public SingleFileExtensionLoadContext(
        string extensionId,
        SingleFileBuild build,
        string packagesFolder
    )
        : base($"single-file:{extensionId}", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagesFolder);

        _buildDirectory = build.BuildPath;
        foreach (SingleFileBuildPackage package in build.Manifest.Packages)
        {
            foreach (string asset in package.Runtime)
            {
                Map(package, asset, packagesFolder, native: false);
            }

            foreach (string asset in package.Native)
            {
                Map(package, asset, packagesFolder, native: true);
            }
        }
    }

    /// <summary>The packages whose copy of a host-owned assembly this context ignored, in lock order.</summary>
    public IReadOnlyList<string> ShadowedPackages => _shadowedPackages;

    /// <summary>
    /// The host-owned assemblies those packages shipped, in <see cref="HostOwnedAssemblies.Names"/>
    /// order, so the one warning reads the same whatever order the lock listed them in.
    /// </summary>
    public IReadOnlyList<string> ShadowedNames =>
        [.. HostOwnedAssemblies.Names.Where(_shadowedNames.Contains)];

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (HostOwnedAssemblies.IsHostOwned(assemblyName.Name) || assemblyName.Name is null)
        {
            return null;
        }

        if (_assemblies.TryGetValue(assemblyName.Name, out string? asset) && File.Exists(asset))
        {
            return LoadFromAssemblyPath(asset);
        }

        string candidate = Path.Combine(_buildDirectory, assemblyName.Name + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        if (_native.TryGetValue(unmanagedDllName, out string? path) && File.Exists(path))
        {
            return LoadUnmanagedDllFromPath(path);
        }

        return IntPtr.Zero;
    }

    private void Map(
        SingleFileBuildPackage package,
        string asset,
        string packagesFolder,
        bool native
    )
    {
        string fileName = Path.GetFileName(asset);
        string name = Path.GetFileNameWithoutExtension(asset);
        if (HostOwnedAssemblies.IsHostOwned(name))
        {
            if (!_shadowedPackages.Contains(package.Id, StringComparer.OrdinalIgnoreCase))
            {
                _shadowedPackages.Add(package.Id);
            }

            _shadowedNames.Add(name);
            return;
        }

        Dictionary<string, string> map = native ? _native : _assemblies;
        map.TryAdd(
            native ? fileName : name,
            SingleFilePackages.AssetPath(packagesFolder, package.Id, package.Version, asset)
        );
    }
}

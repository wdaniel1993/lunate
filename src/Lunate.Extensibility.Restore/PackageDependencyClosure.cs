using Lunate.Extensibility;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// One resolved dependency and the exact version the walk chose, as the identity lines print it
/// (spec: "Install output and <c>--dry-run</c>").
/// </summary>
internal sealed record ResolvedDependency(string Id, string Version);

/// <summary>
/// One assembly the closure contributes: the package that ships it, where it lands relative to the
/// extension directory (a satellite keeps its culture folder), and where the package's copy lies in
/// the global packages folder.
/// </summary>
internal sealed record PackageAssembly(string PackageId, string RelativePath, string SourcePath);

/// <summary>
/// A package's whole dependency closure (design D3–D5): the graph the lock records, the assemblies to
/// materialise, the versions to print, and the declared edges the walk started from. Empty for a
/// package that declares nothing — the common case, where the install has no dependencies to answer
/// for and still writes a lock whose graph is empty.
/// </summary>
internal sealed record PackageClosure(
    IReadOnlyList<ExtensionLockPackage> Packages,
    IReadOnlyList<PackageAssembly> Assemblies,
    IReadOnlyList<ResolvedDependency> Dependencies,
    IReadOnlyList<string> Declared
)
{
    internal static PackageClosure Empty { get; } = new([], [], [], []);
}

/// <summary>
/// Resolves the dependencies a package's nuspec declares through the walk a single-file extension
/// already uses (design D3–D5): the same <see cref="NuGetDependencyWalker"/>, the same conformance
/// rules, the same global packages folder, the same asset selection — and, per package, the same lock
/// entry. What it adds is the materialisation list: the runtime and native assets each resolved
/// package contributes, which the installer copies beside the entry assembly.
/// <para>
/// Two exclusions apply, and both are the load rule the host already has. An assembly the host owns
/// is never materialised and never named in the lock's asset lists, and a package whose id is one of
/// those assemblies is not part of the graph at all: the host supplies it, so downloading and
/// recording it would describe a copy no load would ever bind.
/// </para>
/// </summary>
internal sealed class PackageClosureResolver(
    NuGetPackageFeed feed,
    string globalPackagesFolder,
    SingleFileTarget target
)
{
    private static readonly FrameworkReducer Reducer = new();

    /// <summary>
    /// The closure for <paramref name="package"/>, downloading no more than the walk needs into the
    /// global packages folder. Throws the walker's own refusal — the dependency, the ranges, the feeds
    /// tried — when a dependency is missing or the graph cannot settle.
    /// </summary>
    internal async Task<PackageClosure> ResolveAsync(
        PackageInfo package,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(package);

        IReadOnlyList<ExtensionPackageReference> declared = Declared(package);
        if (declared.Count == 0)
        {
            return PackageClosure.Empty;
        }

        NuGetFramework framework = NuGetFramework.Parse(target.Tfm);
        IReadOnlyList<NuGetDependencyWalker.Resolved> resolved = await new NuGetDependencyWalker(
            feed,
            framework
        )
            .ResolveAsync(declared, cancellationToken)
            .ConfigureAwait(false);

        VersionFolderPathResolver resolver = new(globalPackagesFolder);
        List<ExtensionLockPackage> graph = [];
        List<PackageAssembly> assemblies = [];
        List<ResolvedDependency> dependencies = [];
        foreach (NuGetDependencyWalker.Resolved choice in resolved)
        {
            if (HostOwnedAssemblies.IsHostOwned(choice.Id))
            {
                // The host provides this assembly: the graph must not name it, and there is nothing
                // to materialise. Its own dependencies were walked and are recorded separately.
                continue;
            }

            await feed.InstallAsync(
                    choice.Id,
                    choice.Version,
                    globalPackagesFolder,
                    cancellationToken
                )
                .ConfigureAwait(false);
            string nupkg = resolver.GetPackageFilePath(choice.Id, choice.Version);
            NuGetPackageAssets.Selection assets = NuGetPackageAssets.Read(
                nupkg,
                framework,
                target.Rid
            );
            List<string> analyzers = WithoutHostOwned(assets.Analyzers);
            List<string> runtime = WithoutHostOwned(assets.Runtime);
            List<string> native = WithoutHostOwned(assets.Native);

            graph.Add(
                new ExtensionLockPackage
                {
                    Id = choice.Id,
                    Version = choice.Version.ToNormalizedString(),
                    Sha512 = Sha512(nupkg),
                    Requested = choice.Requested,
                    Direct = choice.Direct,
                    Dependencies =
                    [
                        .. choice.Dependencies.Select(dependency => new ExtensionLockDependency(
                            dependency.Id,
                            (dependency.VersionRange ?? VersionRange.All).ToNormalizedString()
                        )),
                    ],
                    Files = assets.Files,
                    Compile = WithoutHostOwned(assets.Compile),
                    Runtime = runtime,
                    Native = native,
                    Analyzers = analyzers,
                    Generators = analyzers.Count > 0,
                }
            );
            dependencies.Add(
                new ResolvedDependency(choice.Id, choice.Version.ToNormalizedString())
            );

            string installPath = resolver.GetInstallPath(choice.Id, choice.Version);
            foreach (
                string asset in runtime
                    .Concat(native)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
            )
            {
                assemblies.Add(
                    new PackageAssembly(
                        choice.Id,
                        Destination(asset),
                        Path.Combine(installPath, asset.Replace('/', Path.DirectorySeparatorChar))
                    )
                );
            }
        }

        return new PackageClosure(
            graph,
            assemblies,
            dependencies,
            // The declarations the graph started from, minus the ones the host satisfies: the lock's
            // source block is part of the graph, so naming a package the graph excludes would say
            // the host's copy came from the feed.
            [
                .. declared
                    .Where(reference => !HostOwnedAssemblies.IsHostOwned(reference.Id))
                    .Select(reference => reference.Written),
            ]
        );
    }

    /// <summary>
    /// Where one package asset lands relative to the extension directory. An assembly keeps whatever
    /// sits below its framework folder — a satellite lives under its culture, and a load looks for it
    /// there — while a native library lands flat, because a <c>DllImport</c> probes beside the
    /// assembly that asks for it.
    /// </summary>
    private static string Destination(string asset)
    {
        string[] segments = asset.Split('/');
        if (segments.Length > 2 && string.Equals(segments[0], "lib", StringComparison.Ordinal))
        {
            return string.Join('/', segments[2..]);
        }

        if (
            segments.Length > 4
            && string.Equals(segments[0], "runtimes", StringComparison.Ordinal)
            && string.Equals(segments[2], "lib", StringComparison.Ordinal)
        )
        {
            return string.Join('/', segments[4..]);
        }

        return segments[^1];
    }

    /// <summary>
    /// The package's declared edges for this framework, as declarations the walker resolves: the
    /// nearest dependency group, the way NuGet picks one, so a package that declares per-framework
    /// dependencies is resolved for the framework this host runs.
    /// </summary>
    private IReadOnlyList<ExtensionPackageReference> Declared(PackageInfo package)
    {
        IReadOnlyList<PackageDependencyGroup> groups = package.DependencyGroups;
        if (groups.Count == 0)
        {
            return [];
        }

        NuGetFramework? nearest = Reducer.GetNearest(
            NuGetFramework.Parse(target.Tfm),
            groups.Select(group => group.TargetFramework)
        );
        PackageDependencyGroup? group = nearest is null
            ? null
            : groups.FirstOrDefault(candidate => candidate.TargetFramework.Equals(nearest));
        if (group is null)
        {
            return [];
        }

        return
        [
            .. group.Packages.Select(dependency =>
            {
                string range = (dependency.VersionRange ?? VersionRange.All).ToNormalizedString();
                return new ExtensionPackageReference(
                    dependency.Id,
                    range,
                    FormattableString.Invariant($"{dependency.Id}@{range}")
                );
            }),
        ];
    }

    /// <summary>The assets a package contributes once the host-owned ones are dropped.</summary>
    private static List<string> WithoutHostOwned(IReadOnlyList<string> assets) =>
        [
            .. assets.Where(asset =>
                !HostOwnedAssemblies.IsHostOwned(Path.GetFileNameWithoutExtension(asset))
            ),
        ];

    private static string Sha512(string nupkgPath) =>
        Convert.ToBase64String(
            System.Security.Cryptography.SHA512.HashData(File.ReadAllBytes(nupkgPath))
        );
}

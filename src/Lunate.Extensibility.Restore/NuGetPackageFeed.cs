using Lunate.Extensibility;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// The feeds one restore may read, and the only place that talks to them. Every call the engine
/// makes into a feed goes through here, which is what makes "zero feed requests" an observation at
/// the seam rather than an inference from timing: the observer is invoked immediately before the
/// NuGet client is asked for versions or bits.
/// </summary>
internal sealed class NuGetPackageFeed
{
    private readonly List<(PackageSource Source, SourceRepository Repository)> _sources;
    private readonly Action<string>? _observer;
    private readonly Dictionary<string, IReadOnlyList<NuGetVersion>> _versions = new(
        StringComparer.OrdinalIgnoreCase
    );

    private NuGetPackageFeed(
        List<(PackageSource, SourceRepository)> sources,
        Action<string>? observer
    )
    {
        _sources = sources;
        _observer = observer;
    }

    /// <summary>
    /// The feeds for a restore: the explicit ones when configured, otherwise NuGet's own chain
    /// (machine, user and project <c>nuget.config</c>) rooted at the extension's directory. A feed
    /// that appears twice under one URL is used once.
    /// </summary>
    internal static NuGetPackageFeed Create(
        string settingsRoot,
        IReadOnlyList<SingleFilePackageSource> explicitSources,
        Action<string>? observer
    )
    {
        IEnumerable<PackageSource> configured =
            explicitSources.Count > 0
                ? explicitSources.Select(source => new PackageSource(source.Url, source.Name)
                {
                    IsEnabled = true,
                })
                : LoadConfiguredSources(settingsRoot);

        List<(PackageSource, SourceRepository)> sources = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (PackageSource source in configured)
        {
            if (!seen.Add(source.Source))
            {
                continue;
            }

            sources.Add((source, Repository.Factory.GetCoreV3(source)));
        }

        return new NuGetPackageFeed(sources, observer);
    }

    /// <summary>The feed names, sorted, as the lock records them.</summary>
    internal IReadOnlyList<string> Names =>
        [.. _sources.Select(entry => entry.Source.Name).Order(StringComparer.Ordinal)];

    /// <summary>Every version of <paramref name="id"/> any feed offers, oldest first.</summary>
    internal async Task<IReadOnlyList<NuGetVersion>> VersionsAsync(
        string id,
        CancellationToken cancellationToken
    )
    {
        if (_versions.TryGetValue(id, out IReadOnlyList<NuGetVersion>? cached))
        {
            return cached;
        }

        SortedSet<NuGetVersion> versions = [];
        using var cache = new SourceCacheContext();
        foreach ((PackageSource source, SourceRepository repository) in _sources)
        {
            _observer?.Invoke($"versions {id} from {source.Name}");
            FindPackageByIdResource? resource = await repository
                .GetResourceAsync<FindPackageByIdResource>(cancellationToken)
                .ConfigureAwait(false);
            if (resource is null)
            {
                continue;
            }

            IEnumerable<NuGetVersion> found = await resource
                .GetAllVersionsAsync(id, cache, NuGet.Common.NullLogger.Instance, cancellationToken)
                .ConfigureAwait(false);
            foreach (NuGetVersion version in found)
            {
                versions.Add(version);
            }
        }

        IReadOnlyList<NuGetVersion> result = [.. versions];
        _versions[id] = result;
        return result;
    }

    /// <summary>The dependency groups a package declares, from the first feed that carries it.</summary>
    internal async Task<IReadOnlyList<PackageDependencyGroup>> DependencyGroupsAsync(
        string id,
        NuGetVersion version,
        CancellationToken cancellationToken
    )
    {
        using var cache = new SourceCacheContext();
        foreach ((PackageSource source, SourceRepository repository) in _sources)
        {
            _observer?.Invoke($"metadata {id} {version.ToNormalizedString()} from {source.Name}");
            FindPackageByIdResource? resource = await repository
                .GetResourceAsync<FindPackageByIdResource>(cancellationToken)
                .ConfigureAwait(false);
            if (resource is null)
            {
                continue;
            }

            FindPackageByIdDependencyInfo? info = await resource
                .GetDependencyInfoAsync(
                    id,
                    version,
                    cache,
                    NuGet.Common.NullLogger.Instance,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (info is not null)
            {
                return info.DependencyGroups;
            }
        }

        throw new InvalidOperationException(
            $"package '{id}' {version.ToNormalizedString()} was not found on "
                + string.Join(", ", Names)
                + "."
        );
    }

    /// <summary>
    /// The package's folder in the global packages folder, installing it first when it is not there.
    /// A package already on disk is hashed where it lies: a cache hit makes no feed request, which
    /// is what keeps a second restore offline.
    /// </summary>
    internal async Task<string> InstallAsync(
        string id,
        NuGetVersion version,
        string globalPackagesFolder,
        CancellationToken cancellationToken
    )
    {
        VersionFolderPathResolver resolver = new(globalPackagesFolder);
        string installPath = resolver.GetInstallPath(id, version);
        string nupkg = resolver.GetPackageFilePath(id, version);
        if (File.Exists(nupkg))
        {
            return installPath;
        }

        using var cache = new SourceCacheContext();
        foreach ((PackageSource source, SourceRepository repository) in _sources)
        {
            FindPackageByIdResource? resource = await repository
                .GetResourceAsync<FindPackageByIdResource>(cancellationToken)
                .ConfigureAwait(false);
            if (resource is null)
            {
                continue;
            }

            _observer?.Invoke($"download {id} {version.ToNormalizedString()} from {source.Name}");
            var extraction = new PackageExtractionContext(
                PackageSaveMode.Defaultv3,
                XmlDocFileSaveMode.None,
                clientPolicyContext: null,
                NuGet.Common.NullLogger.Instance
            );
            var identity = new PackageIdentity(id, version);
            IPackageDownloader? downloader = await resource
                .GetPackageDownloaderAsync(
                    identity,
                    cache,
                    NuGet.Common.NullLogger.Instance,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (downloader is null)
            {
                continue;
            }

            bool installed = await PackageExtractor
                .InstallFromSourceAsync(
                    identity,
                    downloader,
                    resolver,
                    extraction,
                    cancellationToken,
                    Guid.NewGuid()
                )
                .ConfigureAwait(false);
            if (installed || File.Exists(nupkg))
            {
                return installPath;
            }
        }

        throw new InvalidOperationException(
            $"package '{id}' {version.ToNormalizedString()} could not be downloaded from "
                + string.Join(", ", Names)
                + "."
        );
    }

    private static IEnumerable<PackageSource> LoadConfiguredSources(string settingsRoot)
    {
        ISettings settings = Settings.LoadDefaultSettings(settingsRoot);
        return new PackageSourceProvider(settings)
            .LoadPackageSources()
            .Where(source => source.IsEnabled);
    }
}

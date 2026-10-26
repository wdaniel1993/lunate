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
internal sealed partial class NuGetPackageFeed
{
    private readonly List<(PackageSource Source, SourceRepository Repository)> _sources;
    private readonly Action<string>? _observer;
    private readonly bool _offline;
    private readonly string? _globalPackagesFolder;
    private readonly Dictionary<string, IReadOnlyList<NuGetVersion>> _versions = new(
        StringComparer.OrdinalIgnoreCase
    );

    private NuGetPackageFeed(
        List<(PackageSource, SourceRepository)> sources,
        Action<string>? observer,
        bool offline,
        string? globalPackagesFolder
    )
    {
        _sources = sources;
        _observer = observer;
        _offline = offline;
        _globalPackagesFolder = globalPackagesFolder;
    }

    /// <summary>
    /// The feeds for a restore: the explicit ones when configured, otherwise NuGet's own chain
    /// (machine, user and project <c>nuget.config</c>) rooted at the extension's directory. A feed
    /// that appears twice under one URL is used once.
    /// </summary>
    /// <summary>
    /// The same feeds, in offline mode: the configured names stay known — they are what a record
    /// calls its sources — and no call reaches a feed, because every one of them reads the global
    /// packages folder instead, which is where an offline resolution finds its versions and the
    /// dependency groups of the packages it already holds.
    /// </summary>
    internal static NuGetPackageFeed Create(
        string settingsRoot,
        IReadOnlyList<SingleFilePackageSource> explicitSources,
        Action<string>? observer,
        bool offline = false,
        string? globalPackagesFolder = null
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

        return new NuGetPackageFeed(sources, observer, offline, globalPackagesFolder);
    }

    /// <summary>The feed names, sorted, as the lock records them.</summary>
    internal IReadOnlyList<string> Names =>
        [.. _sources.Select(entry => entry.Source.Name).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Every version of <paramref name="id"/> any feed offers, oldest first. Offline, the answer is
    /// the versions the global packages folder holds — the walk sees the same shape of answer and
    /// makes no request, so a closure nobody cached is a refusal naming the dependency rather than a
    /// feed call.
    /// </summary>
    internal async Task<IReadOnlyList<NuGetVersion>> VersionsAsync(
        string id,
        CancellationToken cancellationToken
    )
    {
        if (_versions.TryGetValue(id, out IReadOnlyList<NuGetVersion>? cached))
        {
            return cached;
        }

        if (_offline)
        {
            IReadOnlyList<NuGetVersion> installed = InstalledVersions(id);
            _versions[id] = installed;
            return installed;
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

    /// <summary>
    /// The dependency groups a package declares, from the first feed that carries it. Offline, they
    /// come from the nupkg in the global packages folder: the package is already resolved, so its
    /// own metadata is on disk and no feed is asked.
    /// </summary>
    internal async Task<IReadOnlyList<PackageDependencyGroup>> DependencyGroupsAsync(
        string id,
        NuGetVersion version,
        CancellationToken cancellationToken
    )
    {
        if (_offline)
        {
            return InstalledDependencyGroups(id, version);
        }

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

        if (_offline)
        {
            throw new InvalidOperationException(OfflineMessage(id, version, globalPackagesFolder));
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

    /// <summary>
    /// The exact package's nupkg in the packages folder and the SHA-512 NuGet recorded for it
    /// (design D3, D11). A warm folder is hashed where it lies and makes no feed request. A nupkg
    /// whose bytes do not match that recorded hash is refused before anything is extracted: the
    /// folder holds something NuGet did not install, and an install must not vouch for it.
    /// </summary>
    internal async Task<PackageDownload> DownloadAsync(
        string id,
        NuGetVersion version,
        string globalPackagesFolder,
        CancellationToken cancellationToken
    )
    {
        VersionFolderPathResolver resolver = new(globalPackagesFolder);
        string nupkg = resolver.GetPackageFilePath(id, version);
        string hashPath = resolver.GetHashPath(id, version);
        string label = FormattableString.Invariant(
            $"package '{id}' {version.ToNormalizedString()}"
        );

        if (!File.Exists(nupkg) || !File.Exists(hashPath))
        {
            if (_offline)
            {
                throw new InvalidOperationException(
                    OfflineMessage(id, version, globalPackagesFolder)
                );
            }

            try
            {
                await InstallAsync(id, version, globalPackagesFolder, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"{label} could not be downloaded from {string.Join(", ", Names)}: {exception.Message}",
                    exception
                );
            }
        }

        string recorded = File.Exists(hashPath) ? ReadText(hashPath).Trim() : string.Empty;
        string computed = Sha512(nupkg);
        if (recorded.Length > 0 && !string.Equals(recorded, computed, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{label}: the nupkg at '{nupkg}' does not match the SHA-512 NuGet recorded in "
                    + $"'{hashPath}'; the packages folder holds bytes NuGet did not install, so "
                    + "nothing was written. Delete "
                    + $"'{Path.GetDirectoryName(nupkg)}' and run the install again."
            );
        }

        return new PackageDownload(
            Path.GetFullPath(nupkg),
            recorded.Length > 0 ? recorded : computed
        );
    }

    /// <summary>The one message a cold packages folder produces, naming the package and the folder.</summary>
    private static string OfflineMessage(
        string id,
        NuGetVersion version,
        string globalPackagesFolder
    ) =>
        FormattableString.Invariant(
            $"package '{id}' {version.ToNormalizedString()} is not in the packages folder "
        )
        + $"'{globalPackagesFolder}'; an offline install reads only packages that are already there, "
        + "so download it once with a feed or point NUGET_PACKAGES at a folder that holds it.";

    private static string Sha512(string nupkgPath)
    {
        using FileStream stream = new(
            nupkgPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        return Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(stream));
    }

    private static string ReadText(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IEnumerable<PackageSource> LoadConfiguredSources(string settingsRoot)
    {
        ISettings settings = Settings.LoadDefaultSettings(settingsRoot);
        return new PackageSourceProvider(settings)
            .LoadPackageSources()
            .Where(source => source.IsEnabled);
    }
}

/// <summary>
/// One downloaded package: the nupkg in the packages folder and the SHA-512 NuGet recorded for it —
/// transport integrity against the feed, not a signature (design D11).
/// </summary>
internal sealed record PackageDownload(string NupkgPath, string Sha512);

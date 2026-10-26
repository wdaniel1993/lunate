using Lunate.Extensibility;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// One package read for a command that prints what it would install (spec: "Install output and
/// <c>--dry-run</c>"): the facts and the feeds they came from, plus the dependency closure in the
/// versions it resolves to. Nothing under the state root is written, and no record is created — a
/// description is not an install, so it writes no lock either.
/// </summary>
public sealed record PackageDescription(PackageInfo Package, IReadOnlyList<string> Sources)
{
    /// <summary>The closure's exact versions, ordered by id; empty for a package with none.</summary>
    public IReadOnlyList<ResolvedDependency> Dependencies { get; init; } = [];
}

public sealed partial class PackageInstaller
{
    /// <summary>
    /// Resolves, downloads and reads one package without writing anything under the state root. The
    /// nupkg and the closure land in the global packages folder — the same cache a restore fills and
    /// the user's SDK reads — and the archive is never extracted, so a dry run resolves the whole
    /// closure and still writes nothing.
    /// </summary>
    public async Task<PackageDescription> DescribeAsync(
        PackageInstallRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        NuGetVersion? exact = TryExactVersion(request.Version);
        string settingsRoot = _options.SettingsRoot ?? Environment.CurrentDirectory;
        string packagesFolder = PackagesFolder(settingsRoot);
        NuGetPackageFeed feed = NuGetPackageFeed.Create(
            settingsRoot,
            _options.Sources,
            _options.OnFeedRequest,
            _options.Offline,
            packagesFolder
        );
        if (feed.Names.Count == 0 && !_options.Offline)
        {
            throw new InvalidOperationException(
                "no package source is configured; add one to a nuget.config above the working "
                    + "directory and run the install again."
            );
        }

        NuGetVersion version =
            exact
            ?? await LatestStableAsync(feed, request.Id, cancellationToken).ConfigureAwait(false);
        string label = FormattableString.Invariant(
            $"package '{request.Id}' {version.ToNormalizedString()}"
        );

        PackageDownload download = await feed.DownloadAsync(
                request.Id,
                version,
                packagesFolder,
                cancellationToken
            )
            .ConfigureAwait(false);
        PackageInfo package = PackageReader.Read(download.NupkgPath);
        RequireIdentity(package, request.Id, version, label);

        PackageClosure closure = await ClosureAsync(
                package,
                feed,
                packagesFolder,
                label,
                cancellationToken
            )
            .ConfigureAwait(false);

        return new PackageDescription(package, feed.Names)
        {
            Dependencies = [.. closure.Dependencies],
        };
    }
}

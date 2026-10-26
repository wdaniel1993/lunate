using System.Globalization;
using Lunate.Extensibility;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// Puts one exact package version on the machine (design D3, D9, D11). The order is fixed: read a
/// record that is already there, download and verify the exact package through
/// <see cref="NuGetPackageFeed"/>, read it with <see cref="PackageReader"/> without running anything,
/// extract into staging, commit the files, and write the record last — so a failure at any point
/// before the record leaves no content that no record names and the next run starts clean.
/// <para>
/// An install writes no trust, compiles nothing and resolves no dependency in this phase: it is a
/// download plus a scope-aware content write, and the graph walk joins it through the same feed.
/// </para>
/// </summary>
public sealed partial class PackageInstaller
{
    private readonly PackageInstallOptions _options;

    public PackageInstaller(PackageInstallOptions? options = null) =>
        _options = options ?? new PackageInstallOptions();

    public async Task<PackageInstallResult> InstallAsync(
        PackageInstallRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        NuGetVersion? exact = TryExactVersion(request.Version);
        PackageInstallTarget target = request.Target;

        PackageInstallRecord? installed = PackageInstallRecordJson.TryRead(
            target.RecordPath,
            out string? problem
        );
        if (problem is not null)
        {
            throw new InvalidDataException(problem);
        }

        if (installed is not null)
        {
            if (!IsSameScope(installed, target))
            {
                throw new InvalidOperationException(
                    FormattableString.Invariant(
                        $"package '{request.Id}' {installed.Package.Version} is already installed for the {ScopeText(installed)} scope (record '{target.RecordPath}'); a package has one record and one set of content, so installing it for the other scope needs 'lunate uninstall "
                    ) + $"{installed.Package.Id}' first."
                );
            }

            if (exact is null || IsSameRequest(installed, request.Id, exact))
            {
                return new PackageInstallResult
                {
                    Status = PackageInstallStatus.AlreadyInstalled,
                    Record = installed,
                    RecordPath = target.RecordPath,
                };
            }

            throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"package '{request.Id}' is already installed at {installed.Package.Version} "
                )
                    + $"(record '{target.RecordPath}'); this version installs one version at a "
                    + FormattableString.Invariant(
                        $"time, so {exact.ToNormalizedString()} cannot replace it — remove the installed package and install the version you want."
                    )
            );
        }

        string settingsRoot = _options.SettingsRoot ?? Environment.CurrentDirectory;
        string packagesFolder = PackagesFolder(settingsRoot);
        NuGetPackageFeed feed = NuGetPackageFeed.Create(
            settingsRoot,
            _options.Sources,
            _options.OnFeedRequest,
            _options.Offline
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

        List<string> written = [];
        List<string> created = [];
        try
        {
            PackageDownload download = await feed.DownloadAsync(
                    request.Id,
                    version,
                    packagesFolder,
                    cancellationToken
                )
                .ConfigureAwait(false);
            PackageInfo package = PackageReader.Read(download.NupkgPath);
            RequireIdentity(package, request.Id, version, label);
            string? worktree = request.Worktree ?? target.Worktree;
            RequireExtensionIdFree(package, target, worktree);
            List<string> warnings = PlaceSkills(package, target, worktree);

            Delete(target.StagingDirectory);
            IReadOnlyList<PackageContentStager.Staged> staged = PackageContentStager.Stage(
                package,
                target,
                target.StagingDirectory
            );
            Commit(staged, target.ScopeRoot, written, created);

            PackageInstallRecord record = new()
            {
                Package = new InstalledPackage
                {
                    Id = package.Id,
                    Version = package.Version,
                    Sha512 = download.Sha512,
                    Sources = feed.Names,
                    Files = package.Files,
                    ExtensionId = package.Extension?.Manifest.Id,
                },
                Content = new InstalledContent
                {
                    ExtensionId = package.Extension?.Manifest.Id,
                    ExtensionVersion = package.Extension?.Manifest.Version,
                    Skills = [.. package.Skills.Select(skill => skill.Name)],
                    Paths =
                    [
                        .. staged.Select(file => new InstalledFile(
                            file.RelativePath,
                            file.Size,
                            file.Sha256
                        )),
                    ],
                },
                Scope = target.Scope,
                Worktree = target.Worktree,
                InstalledAt = Now(),
            };
            PackageInstallRecordJson.WriteTo(target.RecordPath, record);

            return new PackageInstallResult
            {
                Status = PackageInstallStatus.Installed,
                Record = record,
                RecordPath = target.RecordPath,
                WrittenPaths = [.. written],
                Warnings = warnings,
                Resolved = package,
            };
        }
        catch
        {
            Rollback(written, created, target);
            throw;
        }
        finally
        {
            Delete(target.StagingDirectory);
        }
    }

    /// <summary>
    /// The exact version, or null when the request names none and the latest stable version is
    /// resolved from the feeds (design D1). A range or a wildcard is refused here, before any feed
    /// is asked.
    /// </summary>
    private static NuGetVersion? TryExactVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        if (
            version.Contains(',', StringComparison.Ordinal)
            || version.Contains('*', StringComparison.Ordinal)
            || !NuGetVersion.TryParse(version, out NuGetVersion? parsed)
        )
        {
            throw new ArgumentException(
                $"'{version}' is not an exact version; install an exact version such as 1.4.2 — a "
                    + "range or a wildcard belongs to a dependency's declaration, not to an install.",
                nameof(version)
            );
        }

        return parsed;
    }

    /// <summary>
    /// The latest stable version the feeds serve, resolved once and recorded exactly (design D1):
    /// a bare install never re-resolves, and a package no feed serves names the id and the feeds.
    /// </summary>
    private static async Task<NuGetVersion> LatestStableAsync(
        NuGetPackageFeed feed,
        string id,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<NuGetVersion> versions = await feed.VersionsAsync(id, cancellationToken)
            .ConfigureAwait(false);
        NuGetVersion? latest = versions
            .Where(version => !version.IsPrerelease)
            .OrderByDescending(version => version)
            .FirstOrDefault();
        if (latest is null)
        {
            throw new InvalidOperationException(
                $"package '{id}' publishes no stable version on {string.Join(", ", feed.Names)}; "
                    + "name an exact prerelease version to install one."
            );
        }

        return latest;
    }

    /// <summary>
    /// The nupkg must carry the id and version it was installed as; a feed that serves something else
    /// is refused before any file is written.
    /// </summary>
    private static void RequireIdentity(
        PackageInfo package,
        string id,
        NuGetVersion version,
        string label
    )
    {
        bool sameId = string.Equals(package.Id, id, StringComparison.OrdinalIgnoreCase);
        bool sameVersion =
            NuGetVersion.TryParse(package.Version, out NuGetVersion? actual) && actual == version;
        if (!sameId || !sameVersion)
        {
            throw new InvalidDataException(
                $"{label}: the downloaded nupkg identifies itself as '{package.Id}' "
                    + $"{package.Version}; a package must carry the id and version it is installed "
                    + "as, so nothing was written."
            );
        }
    }

    private static bool IsSameRequest(
        PackageInstallRecord record,
        string id,
        NuGetVersion version
    ) =>
        string.Equals(record.Package.Id, id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            record.Package.Version,
            version.ToNormalizedString(),
            StringComparison.Ordinal
        );

    /// <summary>
    /// Whether a record describes an install into this target's scope: the same scope, and for a
    /// project install the same worktree. A record lives at one path per package id, so a second
    /// scope cannot be represented and is refused rather than silently reported installed.
    /// </summary>
    private static bool IsSameScope(PackageInstallRecord record, PackageInstallTarget target)
    {
        if (record.Scope != target.Scope)
        {
            return false;
        }

        if (record.Scope != PackageScope.Project)
        {
            return true;
        }

        StringComparison comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return string.Equals(
            record.Worktree ?? string.Empty,
            target.Worktree ?? string.Empty,
            comparison
        );
    }

    private static string ScopeText(PackageInstallRecord record) =>
        record.Scope == PackageScope.Project ? "project" : "user";

    private string Now() =>
        (_options.Clock ?? TimeProvider.System)
            .GetUtcNow()
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);

    private string PackagesFolder(string settingsRoot) =>
        GlobalPackagesFolder.ResolveFor(_options.GlobalPackagesFolder, settingsRoot);
}

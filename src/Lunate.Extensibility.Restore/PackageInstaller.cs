using System.Globalization;
using Lunate.Extensibility;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// Puts one exact package version on the machine (design D3, D9, D11). The order is fixed: read a
/// record that is already there, download and verify the exact package through
/// <see cref="NuGetPackageFeed"/>, read it with <see cref="PackageReader"/> without running anything,
/// resolve the dependencies it declares through the existing walk, extract into staging, materialise
/// the closure beside the entry assembly, commit the files, write the lock and write the record last
/// — so a failure at any point before the record leaves no content that no record names and the next
/// run starts clean.
/// <para>
/// An install writes no trust, compiles nothing and writes no build: it is a download, a graph walk
/// and a scope-aware content write. The graph's lock is the single-file lock, in the same cache entry
/// a single-file extension uses, because it is the same resolution machinery.
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
                    Dependencies = DependenciesFromLock(target, installed),
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

        List<string> written = [];
        List<string> created = [];
        LockSnapshot? lockSnapshot = null;
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

            // The walk runs before a file lands: a closure no feed serves is a refusal with nothing
            // written, not a half-installed package.
            PackageClosure closure = await ClosureAsync(
                    package,
                    feed,
                    packagesFolder,
                    label,
                    cancellationToken
                )
                .ConfigureAwait(false);

            Delete(target.StagingDirectory);
            IReadOnlyList<PackageContentStager.Staged> staged = PackageContentStager.Stage(
                package,
                target,
                target.StagingDirectory
            );
            staged = Materialise(closure, package, target, target.StagingDirectory, staged);
            Commit(staged, target.ScopeRoot, written, created);

            // The lock, then the record: a record that exists names content that exists, and a lock
            // no record names cannot survive a failure.
            lockSnapshot = package.Extension is null
                ? null
                : WriteLock(package, closure, feed.Names, target);

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
                Dependencies = [.. closure.Dependencies],
            };
        }
        catch
        {
            lockSnapshot?.Restore();
            Rollback(written, created, target);
            throw;
        }
        finally
        {
            Delete(target.StagingDirectory);
        }
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

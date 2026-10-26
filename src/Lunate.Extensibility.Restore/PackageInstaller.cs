using System.Globalization;
using Lunate.Ai;
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
internal sealed partial class PackageInstaller
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

        // One mutation at a time for this package, from the record this run reads to the record it
        // writes: an install and an uninstall take the same claim, so neither stages, commits or
        // records while the other is half-way through its own.
        using FileLock lease = PackageOperationLock.Acquire(target.RecordPath, request.Id);
        ClearStaging(target);

        PackageInstallRecord? installed = PackageInstallRecordJson.TryRead(
            target.RecordPath,
            out string? problem
        );

        // A record that cannot be read is not a refusal: the addendum makes a reinstall the repair,
        // so this run writes the content and a new record over it and names, afterwards, the bytes
        // it could not account for. Nothing here claims to have removed what it cannot name.
        string? unreadable = problem;

        // The same version in the same scope is nothing to resolve — unless --force asks for a
        // repair, which rewrites the recorded paths from the recorded version's own archive. A
        // different exact version is the upgrade: a pinned replacement, never a second install.
        bool sameVersion = false;
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

            sameVersion = exact is null || IsSameRequest(installed, request.Id, exact);
            if (sameVersion && !request.Force)
            {
                return new PackageInstallResult
                {
                    Status = PackageInstallStatus.AlreadyInstalled,
                    Record = installed,
                    RecordPath = target.RecordPath,
                    Dependencies = DependenciesFromLock(target, installed),
                    ContentBytes = PackageContentSize.Total(installed.Content.Paths),
                    Warnings = SizeWarnings(target.Scope, installed.Content.Paths),
                };
            }
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

        NuGetVersion version = sameVersion
            ? NuGetVersion.Parse(installed!.Package.Version)
            : exact
                ?? await LatestStableAsync(feed, request.Id, cancellationToken)
                    .ConfigureAwait(false);
        string label = FormattableString.Invariant(
            $"package '{request.Id}' {version.ToNormalizedString()}"
        );

        List<string> written = [];
        List<string> created = [];
        LockSnapshot? lockSnapshot = null;
        PreviousContent? previous = null;
        string staging = target.NewStagingDirectory();
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

            // An install replaces its own previous content, so the paths and directories the old
            // record names are not a collision — they are what this run takes back. An unreadable
            // record names nothing, and the paths this install writes at this scope are still its
            // own to replace.
            bool broken = unreadable is not null;
            RequireExtensionIdFree(package, target, worktree, installed, broken);
            List<string> warnings = PlaceSkills(package, target, worktree, installed, broken);

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

            IReadOnlyList<PackageContentStager.Staged> staged = PackageContentStager.Stage(
                package,
                target,
                staging
            );
            staged = Materialise(closure, package, target, staging, staged);

            if (installed is not null && sameVersion)
            {
                // --force on an installed version repairs it: the archive and the closure are
                // staged, the record is not rewritten, and only the files that no longer answer for
                // their recorded hash are copied back.
                return new PackageInstallResult
                {
                    Status = PackageInstallStatus.Repaired,
                    Record = installed,
                    RecordPath = target.RecordPath,
                    WrittenPaths = Repair(installed, target, staged, label),
                    Warnings =
                    [
                        .. warnings,
                        .. SizeWarnings(target.Scope, installed.Content.Paths),
                    ],
                    Resolved = package,
                    Dependencies = [.. closure.Dependencies],
                    ContentBytes = PackageContentSize.Total(installed.Content.Paths),
                };
            }

            // Before the first file moves, keep what the old record names, so a failure after this
            // point puts the previous install back rather than leaving a package with no content.
            previous = installed is null
                ? null
                : PreviousContent.Capture(installed, target, staging);

            Commit(staged, target.ScopeRoot, written, created);
            List<string> leftBehind = [];
            List<string> removed = installed is null
                ? []
                : RemovePrevious(installed, target, staged, leftBehind);

            // The lock, then the record: a record that exists names content that exists, and a lock
            // no record names cannot survive a failure.
            lockSnapshot = package.Extension is null
                ? null
                : WriteLock(package, closure, feed.Names, target);

            PackageInstallRecord record = NewRecord(
                package,
                staged,
                download.Sha512,
                feed.Names,
                target,
                Now()
            );
            PackageInstallRecordJson.WriteTo(target.RecordPath, record);

            if (unreadable is not null)
            {
                warnings.AddRange(Unaccounted(unreadable, target, package, staged));
            }

            warnings.AddRange(SizeWarnings(target.Scope, record.Content.Paths));

            return new PackageInstallResult
            {
                Status = installed is null
                    ? PackageInstallStatus.Installed
                    : PackageInstallStatus.Upgraded,
                Record = record,
                RecordPath = target.RecordPath,
                WrittenPaths = [.. written],
                Warnings = warnings,
                Resolved = package,
                Dependencies = [.. closure.Dependencies],
                PreviousVersion = installed?.Package.Version,
                RemovedPaths = removed,
                LeftBehind = leftBehind,
                ContentBytes = PackageContentSize.Total(record.Content.Paths),
            };
        }
        catch
        {
            lockSnapshot?.Restore();
            Rollback(written, created, target, staging, previous);
            throw;
        }
        finally
        {
            Delete(staging);
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

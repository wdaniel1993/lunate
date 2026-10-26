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
/// download plus a content write, and the graph walk joins it through the same feed.
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

        NuGetVersion version = ExactVersion(request.Version);
        PackageInstallTarget target = request.Target;
        string label = FormattableString.Invariant(
            $"package '{request.Id}' {version.ToNormalizedString()}"
        );

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
            if (!IsSameRequest(installed, request.Id, version))
            {
                throw new InvalidOperationException(
                    FormattableString.Invariant(
                        $"package '{request.Id}' is already installed at {installed.Package.Version} "
                    )
                        + $"(record '{target.RecordPath}'); this version installs one version at a "
                        + FormattableString.Invariant(
                            $"time, so {version.ToNormalizedString()} cannot replace it — remove the installed package and install the version you want."
                        )
                );
            }

            return new PackageInstallResult
            {
                Status = PackageInstallStatus.AlreadyInstalled,
                Record = installed,
                RecordPath = target.RecordPath,
            };
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

    /// <summary>The exact version, or a refusal naming what an install accepts (design D1).</summary>
    private static NuGetVersion ExactVersion(string version)
    {
        if (
            string.IsNullOrWhiteSpace(version)
            || version.Contains(',', StringComparison.Ordinal)
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

    private string Now() =>
        (_options.Clock ?? TimeProvider.System)
            .GetUtcNow()
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);

    private string PackagesFolder(string settingsRoot) =>
        GlobalPackagesFolder.ResolveFor(_options.GlobalPackagesFolder, settingsRoot);
}

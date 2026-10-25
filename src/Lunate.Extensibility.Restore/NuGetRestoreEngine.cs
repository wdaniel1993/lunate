using System.Security.Cryptography;
using Lunate.Extensibility;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// The in-process restore: NuGet's client libraries resolve the declared packages and their
/// dependencies, install them into the global packages folder (the user's, shared with the SDK),
/// and hand back everything <c>extension.lock.json</c> records. Nothing here is loaded until a
/// restore runs — the assembly is the only place that references <c>NuGet.*</c>.
/// </summary>
public sealed class NuGetRestoreEngine : ISingleFileRestoreEngine
{
    private readonly NuGetRestoreEngineOptions _options;

    /// <summary>Null options means: the standard feed chain and the packages folder NuGet resolves.</summary>
    public NuGetRestoreEngine(NuGetRestoreEngineOptions? options = null) =>
        _options = options ?? new NuGetRestoreEngineOptions();

    /// <summary>
    /// Resolves and installs the request's packages, then the reference pack. Throws with a
    /// user-readable reason when a package is missing, a range is unsatisfiable, or no reference
    /// pack covers the runtime; the restorer turns that into a <c>failed (restore)</c> row.
    /// </summary>
    public async Task<SingleFileRestoreResult> RestoreAsync(
        SingleFileRestoreRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        string settingsRoot = Path.GetDirectoryName(request.SourcePath) ?? request.SourcePath;
        NuGetPackageFeed feed = NuGetPackageFeed.Create(
            settingsRoot,
            _options.Sources,
            _options.OnFeedRequest
        );
        if (feed.Names.Count == 0)
        {
            throw new InvalidOperationException(
                "no package source is configured; add one to a nuget.config above the extension or "
                    + "set NUGET_PACKAGES for an offline cache."
            );
        }

        string globalPackagesFolder = GlobalPackagesFolder.Resolve(
            _options.GlobalPackagesFolder ?? ConfiguredGlobalPackagesFolder(settingsRoot),
            Environment.GetEnvironmentVariable("NUGET_PACKAGES"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        );
        NuGetFramework target = NuGetFramework.Parse(request.Target.Tfm);

        IReadOnlyList<NuGetDependencyWalker.Resolved> resolved = await new NuGetDependencyWalker(
            feed,
            target
        )
            .ResolveAsync(request.Packages, cancellationToken)
            .ConfigureAwait(false);

        VersionFolderPathResolver resolver = new(globalPackagesFolder);
        List<ExtensionLockPackage> packages = [];
        foreach (NuGetDependencyWalker.Resolved package in resolved)
        {
            await feed.InstallAsync(
                    package.Id,
                    package.Version,
                    globalPackagesFolder,
                    cancellationToken
                )
                .ConfigureAwait(false);

            string nupkg = resolver.GetPackageFilePath(package.Id, package.Version);
            NuGetPackageAssets.Selection assets = NuGetPackageAssets.Read(
                nupkg,
                target,
                request.Target.Rid
            );

            packages.Add(
                new ExtensionLockPackage
                {
                    Id = package.Id,
                    Version = package.Version.ToNormalizedString(),
                    Sha512 = Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(nupkg))),
                    Requested = package.Requested,
                    Direct = package.Direct,
                    Dependencies =
                    [
                        .. package.Dependencies.Select(dependency => new ExtensionLockDependency(
                            dependency.Id,
                            (
                                dependency.VersionRange ?? NuGet.Versioning.VersionRange.All
                            ).ToNormalizedString()
                        )),
                    ],
                    Files = assets.Files,
                    Compile = assets.Compile,
                    Runtime = assets.Runtime,
                    Native = assets.Native,
                    Analyzers = assets.Analyzers,
                    Generators = assets.Generators,
                }
            );
        }

        ExtensionLockReferencePack referencePack = await NuGetReferencePackRestore
            .EnsureAsync(feed, globalPackagesFolder, request.Target, cancellationToken)
            .ConfigureAwait(false);

        return new SingleFileRestoreResult
        {
            Packages = packages,
            ReferencePack = referencePack,
            Sources = feed.Names,
            GlobalPackagesFolder = globalPackagesFolder,
        };
    }

    /// <summary><c>globalPackagesFolder</c> from the configuration chain, or null when unset.</summary>
    private static string? ConfiguredGlobalPackagesFolder(string settingsRoot)
    {
        ISettings settings = Settings.LoadDefaultSettings(settingsRoot);
        string? configured = SettingsUtility.GetConfigValue(
            settings,
            "globalPackagesFolder",
            isPath: true
        );
        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }
}

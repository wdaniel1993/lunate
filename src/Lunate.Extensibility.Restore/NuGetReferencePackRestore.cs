using NuGet.Frameworks;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// Gets <c>Microsoft.NETCore.App.Ref</c> for the running runtime. A published self-contained binary
/// has no SDK and no reference pack beside it, so the pack is restored through the same in-process
/// engine into the global packages folder and reused from there: a second restore reads it off disk
/// and makes no feed request. The chosen version and the rule that chose it go into the lock, so a
/// later restore resolves to the same pack instead of re-deciding.
/// </summary>
internal static class NuGetReferencePackRestore
{
    /// <summary>
    /// The pack for <paramref name="target"/>, restoring it only when the global packages folder has
    /// no pack the rule would pick.
    /// </summary>
    internal static async Task<ExtensionLockReferencePack> EnsureAsync(
        NuGetPackageFeed feed,
        string globalPackagesFolder,
        SingleFileTarget target,
        CancellationToken cancellationToken
    )
    {
        string tfm = target.Tfm;
        Version runtime = Version.Parse(target.Runtime);

        (string Version, string Rule)? cached = ReferencePackRule.Select(
            runtime,
            InstalledVersions(globalPackagesFolder, tfm)
        );
        if (cached is { } available)
        {
            return Pack(globalPackagesFolder, available.Version, available.Rule, tfm);
        }

        IReadOnlyList<NuGet.Versioning.NuGetVersion> versions = await feed.VersionsAsync(
                ReferencePackRule.PackageId,
                cancellationToken
            )
            .ConfigureAwait(false);
        (string Version, string Rule)? selected = ReferencePackRule.Select(
            runtime,
            versions.Select(version => version.ToNormalizedString())
        );
        if (selected is not { } choice)
        {
            throw new InvalidOperationException(
                $"no {ReferencePackRule.PackageId} is published for runtime {target.Runtime} on "
                    + $"({string.Join(", ", feed.Names)}); a reference pack is required to compile."
            );
        }

        await feed.InstallAsync(
                ReferencePackRule.PackageId,
                NuGet.Versioning.NuGetVersion.Parse(choice.Version),
                globalPackagesFolder,
                cancellationToken
            )
            .ConfigureAwait(false);
        return Pack(globalPackagesFolder, choice.Version, choice.Rule, tfm);
    }

    /// <summary>The pack's lock entry, with the <c>ref/&lt;tfm&gt;</c> assemblies it contributes.</summary>
    private static ExtensionLockReferencePack Pack(
        string globalPackagesFolder,
        string version,
        string rule,
        string tfm
    )
    {
        NuGet.Packaging.VersionFolderPathResolver resolver = new(globalPackagesFolder);
        string installPath = resolver.GetInstallPath(
            ReferencePackRule.PackageId,
            NuGet.Versioning.NuGetVersion.Parse(version)
        );
        List<string> assets = Assets(installPath, tfm);
        if (assets.Count == 0)
        {
            throw new InvalidOperationException(
                $"{ReferencePackRule.PackageId} {version} has no ref/{tfm} assemblies in "
                    + $"'{installPath}'; it cannot serve as the reference pack for this runtime."
            );
        }

        return new ExtensionLockReferencePack
        {
            Id = ReferencePackRule.PackageId,
            Version = version,
            Rule = rule,
            Assets = assets,
        };
    }

    /// <summary>The versions already extracted in the global packages folder that carry a ref folder.</summary>
    private static IReadOnlyList<string> InstalledVersions(string globalPackagesFolder, string tfm)
    {
        string root = Path.Combine(
            globalPackagesFolder,
            ReferencePackRule.PackageId.ToLowerInvariant()
        );
        if (!Directory.Exists(root))
        {
            return [];
        }

        return
        [
            .. Directory
                .GetDirectories(root)
                .Where(directory => Assets(directory, tfm).Count > 0)
                .Select(Path.GetFileName)
                .OfType<string>(),
        ];
    }

    /// <summary>The <c>ref/&lt;tfm&gt;</c> assemblies, package-relative with forward slashes.</summary>
    private static List<string> Assets(string installPath, string tfm)
    {
        string root = Path.Combine(installPath, "ref", tfm);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return
        [
            .. Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(file =>
                    Path.GetRelativePath(installPath, file)
                        .Replace(Path.DirectorySeparatorChar, '/')
                )
                .Order(StringComparer.Ordinal),
        ];
    }
}

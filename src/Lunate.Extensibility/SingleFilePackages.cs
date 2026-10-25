namespace Lunate.Extensibility;

/// <summary>
/// Where a build's locked packages live, and how a package-relative asset becomes a path. A build
/// records its assets package-relative so it can be read on any machine; turning one into a path is
/// the same rule for the compile and the load, or a build would compile against files the load
/// cannot find. The folder follows the restore engine's precedence: <c>NUGET_PACKAGES</c>, else
/// <c>~/.nuget/packages</c>. A <c>globalPackagesFolder</c> set only in a <c>nuget.config</c> chain is
/// not read here — reading it means loading <c>NuGet.Configuration</c> — so such a machine must set
/// the variable for the load to resolve what the restore installed.
/// </summary>
public static class SingleFilePackages
{
    /// <summary>NuGet's packages folder for this process.</summary>
    public static string DefaultFolder()
    {
        string? configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        return Path.GetFullPath(
            configured is { Length: > 0 } && !string.IsNullOrWhiteSpace(configured)
                ? configured
                : Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile,
                        Environment.SpecialFolderOption.DoNotVerify
                    ),
                    ".nuget",
                    "packages"
                )
        );
    }

    /// <summary>
    /// One locked package's asset as an absolute path: NuGet's lowercase layout, with the asset's
    /// forward slashes turned into this platform's separator.
    /// </summary>
    public static string AssetPath(
        string packagesFolder,
        string packageId,
        string packageVersion,
        string asset
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagesFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset);

        return Path.Combine(
            Path.GetFullPath(packagesFolder),
            packageId.ToLowerInvariant(),
            packageVersion.ToLowerInvariant(),
            asset.Replace('/', Path.DirectorySeparatorChar)
        );
    }
}

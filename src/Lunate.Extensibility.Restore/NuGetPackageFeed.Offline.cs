using NuGet.Packaging;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// The offline half of the feed seam (design D11, spec: "Failure, offline and private feeds"): an
/// install that may not reach a network still has to answer the walk's two questions — which
/// versions of a package exist, and what does one of them depend on — so it answers them from the
/// global packages folder. No call here touches a repository, which is what makes "zero feed
/// requests" an observation rather than a promise: the observer is never invoked.
/// </summary>
internal sealed partial class NuGetPackageFeed
{
    /// <summary>
    /// The versions of <paramref name="id"/> the global packages folder holds, oldest first: NuGet's
    /// own layout (<c>&lt;folder&gt;/&lt;id&gt;/&lt;version&gt;/&lt;id&gt;.&lt;version&gt;.nupkg</c>),
    /// and only an extracted package with its nupkg counts.
    /// </summary>
    private IReadOnlyList<NuGetVersion> InstalledVersions(string id)
    {
        if (Folder() is not { } folder)
        {
            return [];
        }

        string root = Path.Combine(folder, id.ToLowerInvariant());
        var resolver = new VersionFolderPathResolver(folder);
        SortedSet<NuGetVersion> versions = [];
        try
        {
            if (!Directory.Exists(root))
            {
                return [];
            }

            foreach (string directory in Directory.GetDirectories(root))
            {
                if (
                    NuGetVersion.TryParse(Path.GetFileName(directory), out NuGetVersion? version)
                    && File.Exists(resolver.GetPackageFilePath(id, version))
                )
                {
                    versions.Add(version);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed holds nothing this install may use; the walk reports
            // the package as unresolved, which is the refusal the user can act on.
            return [];
        }

        return [.. versions];
    }

    /// <summary>
    /// The dependency groups of a package the global packages folder holds, read from its own nuspec:
    /// the same metadata NuGet serves, without a request. A package whose nupkg is absent is the one
    /// offline refusal, naming the package and the folder.
    /// </summary>
    private IReadOnlyList<PackageDependencyGroup> InstalledDependencyGroups(
        string id,
        NuGetVersion version
    )
    {
        string folder =
            Folder()
            ?? throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"package '{id}' {version.ToNormalizedString()} cannot be read offline: no global "
                ) + "packages folder is configured, so there is nothing to read the package from."
            );
        var resolver = new VersionFolderPathResolver(folder);
        string nupkg = resolver.GetPackageFilePath(id, version);
        if (!File.Exists(nupkg))
        {
            throw new InvalidOperationException(OfflineMessage(id, version, folder));
        }

        using FileStream stream = new(
            nupkg,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var reader = new PackageArchiveReader(stream);
        return [.. reader.NuspecReader.GetDependencyGroups()];
    }

    /// <summary>The packages folder an offline lookup reads, or null when none was configured.</summary>
    private string? Folder() => _globalPackagesFolder is { Length: > 0 } folder ? folder : null;
}

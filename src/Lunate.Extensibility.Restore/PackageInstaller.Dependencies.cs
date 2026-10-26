using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// The dependency half of an install (design D3–D5): the package's declared set goes to the existing
/// walk, the resolved assemblies are staged beside the entry assembly, and the lock is written into
/// the extension's cache entry — the same file, in the same place, a single-file extension uses. None
/// of it compiles anything, and none of it writes a build.
/// <para>
/// The order is what makes "no content that no record names" true: the walk runs before a file
/// lands, so a dependency no feed serves refuses with nothing written; the lock is written after the
/// content commits and before the record; and a failure anywhere before the record puts back the lock
/// this run replaced.
/// </para>
/// </summary>
public sealed partial class PackageInstaller
{
    /// <summary>
    /// The package's closure, or an empty one when it declares nothing. A skills-only package that
    /// declares a dependency is refused here: there is no extension directory to materialise it into
    /// and no cache entry to hold its lock, and installing without it would report a closure that
    /// does not exist.
    /// </summary>
    private async Task<PackageClosure> ClosureAsync(
        PackageInfo package,
        NuGetPackageFeed feed,
        string packagesFolder,
        string label,
        CancellationToken cancellationToken
    )
    {
        if (package.DeclaredDependencies.Count > 0 && package.Extension is null)
        {
            throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"{label}: the package declares the NuGet dependency '{package.DeclaredDependencies[0].Id}' but carries no extension, so there is no extension directory to materialise it into and no cache entry to hold its lock; a skills-only package cannot depend on a package."
                )
            );
        }

        try
        {
            return await new PackageClosureResolver(
                feed,
                packagesFolder,
                SingleFileTarget.Current()
            )
                .ResolveAsync(package, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            // The walker names the dependency and, when a package is missing, the feeds it asked; for
            // a graph that cannot settle it names the ranges, so the install adds the feeds, what a
            // package author can do, and — offline — the one place it was allowed to read.
            string conflict = IsGraphConflict(exception.Message)
                ? FormattableString.Invariant(
                    $" The install resolved this graph against {string.Join(", ", feed.Names)}; a package's dependency set is the one its nuspec declares, so the package has to be republished with ranges that agree."
                )
                : string.Empty;
            string offline = _options.Offline
                ? FormattableString.Invariant(
                    $" An offline install reads only the packages folder '{packagesFolder}' and makes no feed request."
                )
                : string.Empty;
            throw new InvalidOperationException(
                $"{label}: {exception.Message}{conflict}{offline}",
                exception
            );
        }
    }

    /// <summary>
    /// Whether the walker's refusal is a graph that cannot settle — NuGet's downgrade, outside-range
    /// and conflict codes — rather than a package missing from every feed. The distinction decides
    /// whether telling the author to republish is the useful thing to say.
    /// </summary>
    private static bool IsGraphConflict(string message) =>
        message.Contains("NU1605", StringComparison.Ordinal)
        || message.Contains("NU1608", StringComparison.Ordinal)
        || message.Contains("NU1107", StringComparison.Ordinal);

    /// <summary>
    /// Copies the closure's assemblies into staging beside the entry assembly, adding each to the
    /// staged list so the record answers for it and uninstall can take it back. A file the package's
    /// own content already ships is left alone — the publisher chose those bytes — while two
    /// dependencies offering one destination is a refusal, because which one loads would otherwise be
    /// an accident of ordering. Everything a package ships is confined to the extension directory, so
    /// a hostile nuspec cannot write outside it.
    /// </summary>
    private static List<PackageContentStager.Staged> Materialise(
        PackageClosure closure,
        PackageInfo package,
        PackageInstallTarget target,
        string stagingDirectory,
        IReadOnlyList<PackageContentStager.Staged> staged
    )
    {
        if (closure.Assemblies.Count == 0)
        {
            return [.. staged];
        }

        string extension = FormattableString.Invariant(
            $"{target.ExtensionDirectory}/{package.Extension!.Manifest.Id}"
        );
        Dictionary<string, string> taken = new(StringComparer.Ordinal);
        foreach (PackageContentStager.Staged file in staged)
        {
            taken[file.RelativePath] = package.Id;
        }

        List<PackageContentStager.Staged> all = [.. staged];
        foreach (PackageAssembly assembly in closure.Assemblies)
        {
            if (
                PackageInstallRecordJson.TryConfine(
                    FormattableString.Invariant($"{extension}/{assembly.RelativePath}"),
                    out string relative,
                    out string? reason
                )
                is false
            )
            {
                throw new InvalidDataException(
                    FormattableString.Invariant(
                        $"package '{package.Id}' {package.Version}: the dependency '{assembly.PackageId}' would write '{extension}/{assembly.RelativePath}', which does not stay inside the extension directory ({reason}); republish that package."
                    )
                );
            }

            if (taken.TryGetValue(relative, out string? owner))
            {
                if (string.Equals(owner, package.Id, StringComparison.OrdinalIgnoreCase))
                {
                    // The bundle ships its own copy of a dependency's assembly: its bytes are the
                    // ones the publisher chose, so the closure leaves them in place.
                    continue;
                }

                throw new InvalidOperationException(
                    FormattableString.Invariant(
                        $"package '{package.Id}' {package.Version}: the dependencies '{owner}' and '{assembly.PackageId}' both provide '{assembly.RelativePath}', so which bytes load would be an accident of ordering; an extension directory holds one copy of each file. Remove one of them from the closure."
                    )
                );
            }

            string path = Path.Combine(
                stagingDirectory,
                relative.Replace('/', Path.DirectorySeparatorChar)
            );
            string? directory = Path.GetDirectoryName(path);
            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(assembly.SourcePath, path, overwrite: true);
            all.Add(
                new PackageContentStager.Staged(
                    relative,
                    path,
                    new FileInfo(path).Length,
                    Sha256(path)
                )
            );
            taken[relative] = assembly.PackageId;
        }

        all.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return all;
    }

    /// <summary>
    /// The lock a package install writes: the existing format, in the extension's cache entry. The
    /// graph is the closure; the source digest is the nupkg's SHA-512, so the graph answers for the
    /// package's own bytes; the declared list is the package's dependency edges as written; and the
    /// toolset half names the machine the graph was resolved for without a reference pack, because
    /// nothing compiles.
    /// </summary>
    private static ExtensionLock Compose(
        PackageInfo package,
        PackageClosure closure,
        IReadOnlyList<string> sources
    )
    {
        SingleFileTarget target = SingleFileTarget.Current();
        return new ExtensionLock
        {
            ExtensionId = package.Extension!.Manifest.Id,
            ExtensionVersion = package.Extension.Manifest.Version,
            SourceDigest = package.Sha512,
            PackagesWritten = [.. closure.Declared],
            Toolset = new ExtensionLockToolset
            {
                Tfm = target.Tfm,
                Rid = target.Rid,
                Runtime = target.Runtime,
                ReferencePack = new ExtensionLockReferencePack
                {
                    Id = ReferencePackRule.PackageId,
                    Version = target.Runtime,
                    Rule = ReferencePackRules.NotCompiled,
                    Assets = [],
                },
            },
            Sources = [.. sources],
            Packages = [.. closure.Packages],
        };
    }

    /// <summary>The SHA-256 of a staged file, hex lower: the hash the record answers for.</summary>
    private static string Sha256(string path) =>
        Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))
        );

    /// <summary>The lock path for a package's extension: the cache entry every lock already uses.</summary>
    private static string LockPath(PackageInstallTarget target, string extensionId) =>
        SingleFileExtensionPaths.LockPath(target.StoreRoot, extensionId);

    /// <summary>
    /// Writes the lock into the extension's cache entry and hands back what it replaced, so a failure
    /// after this point can put the previous resolution back.
    /// </summary>
    private static LockSnapshot WriteLock(
        PackageInfo package,
        PackageClosure closure,
        IReadOnlyList<string> sources,
        PackageInstallTarget target
    )
    {
        string path = LockPath(target, package.Extension!.Manifest.Id);
        LockSnapshot snapshot = LockSnapshot.Capture(path);
        ExtensionLockJson.WriteTo(path, Compose(package, closure, sources));
        return snapshot;
    }

    /// <summary>
    /// The closure an installed record already resolved, read from the lock it names. A record whose
    /// lock is gone (an install from before this phase, or a pruned cache) answers with nothing
    /// rather than with a guess.
    /// </summary>
    private static IReadOnlyList<ResolvedDependency> DependenciesFromLock(
        PackageInstallTarget target,
        PackageInstallRecord record
    )
    {
        if (record.Package.ExtensionId is not { Length: > 0 } extensionId)
        {
            return [];
        }

        ExtensionLock? @lock = ExtensionLockJson.TryRead(
            LockPath(target, extensionId),
            out string? _
        );
        return @lock is null
            ? []
            :
            [
                .. @lock.Packages.Select(package => new ResolvedDependency(
                    package.Id,
                    package.Version
                )),
            ];
    }

    /// <summary>
    /// The lock as it was before this run wrote over it, so a failed install can put it back: the
    /// record is the commit point, so a lock no record names must not survive a failure.
    /// </summary>
    private sealed record LockSnapshot(string Path, byte[]? Bytes)
    {
        internal static LockSnapshot Capture(string path) =>
            new(path, File.Exists(path) ? File.ReadAllBytes(path) : null);

        internal void Restore()
        {
            try
            {
                if (Bytes is null)
                {
                    if (File.Exists(Path))
                    {
                        File.Delete(Path);
                    }

                    if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } directory)
                    {
                        TryDeleteEmptyDirectory(directory);
                    }

                    return;
                }

                File.WriteAllBytes(Path, Bytes);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // A lock that cannot be put back is rebuilt by the next install; failing the failed
                // install again over it would hide the original reason.
            }
        }
    }
}

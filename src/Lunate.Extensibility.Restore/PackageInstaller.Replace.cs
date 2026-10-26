using System.Globalization;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// Replacing an installed version (spec: "Pinning and upgrade", "Uninstall"). An upgrade is a pinned
/// replacement, not a second install: the content the old record names is taken back through that
/// record — the paths the new version no longer writes are deleted, and the directories they leave
/// empty go with them — before the new record commits the change.
/// <para>
/// The record is the commit point, so the bytes it names survive a failed run: they are copied into
/// a rollback directory inside staging before the first file is touched and put back if anything
/// after that fails. Nothing outside the old record's own paths is removed, so a hand-added file in
/// an installed directory is never an upgrade's to delete.
/// </para>
/// </summary>
public sealed partial class PackageInstaller
{
    /// <summary>
    /// The record one install commits (design D8): the package half from what was downloaded and
    /// read, the content half from what was staged, and the scope and time this run wrote it for.
    /// </summary>
    private static PackageInstallRecord NewRecord(
        PackageInfo package,
        IReadOnlyList<PackageContentStager.Staged> staged,
        string sha512,
        IReadOnlyList<string> sources,
        PackageInstallTarget target,
        string installedAt
    ) =>
        new()
        {
            Package = new InstalledPackage
            {
                Id = package.Id,
                Version = package.Version,
                Sha512 = sha512,
                Sources = sources,
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
            InstalledAt = installedAt,
        };

    /// <summary>
    /// Deletes every path the old record names that the new content does not write, then removes the
    /// directories that are now empty. A path the new version keeps is overwritten by the commit, not
    /// deleted, so two versions that share a file name never leave the directory without it.
    /// </summary>
    private static List<string> RemovePrevious(
        PackageInstallRecord record,
        PackageInstallTarget target,
        IReadOnlyList<PackageContentStager.Staged> staged,
        List<string> leftBehind
    )
    {
        string root = ScopeRoot(record, target.StoreRoot);
        HashSet<string> keep = new(
            staged.Select(file => file.RelativePath),
            StringComparer.Ordinal
        );
        List<string> removed = [];
        List<string> directories = [];
        foreach (InstalledFile file in record.Content.Paths)
        {
            if (keep.Contains(file.Path))
            {
                continue;
            }

            string path = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
            ScopePaths.RequireInside(root, path, "delete");
            if (File.Exists(path))
            {
                // A file Windows still has mapped is not silently dropped: the new record cannot
                // name it, so the run says it stayed behind instead of claiming to have removed it.
                if (!TryDeleteFile(path))
                {
                    leftBehind.Add(file.Path);
                }
            }

            removed.Add(file.Path);
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                directories.Add(directory);
            }
        }

        foreach (
            string directory in directories
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(Depth)
        )
        {
            TryDeleteEmptyDirectory(directory);
        }

        return removed;
    }

    /// <summary>
    /// Rewrites the recorded paths that are missing or no longer match their recorded hash, from the
    /// recorded version's own archive, and writes nothing else (spec: "Failure, offline and private
    /// feeds"). The record is not rewritten: it already names the right bytes, so a repair that
    /// succeeds leaves it byte-identical, and a repair that cannot reproduce them refuses rather than
    /// writing bytes no record names.
    /// </summary>
    private static List<string> Repair(
        PackageInstallRecord record,
        PackageInstallTarget target,
        IReadOnlyList<PackageContentStager.Staged> staged,
        string label
    )
    {
        string root = ScopeRoot(record, target.StoreRoot);
        Dictionary<string, PackageContentStager.Staged> archive = staged.ToDictionary(
            file => file.RelativePath,
            StringComparer.Ordinal
        );
        List<string> repaired = [];
        foreach (InstalledFile file in record.Content.Paths)
        {
            string destination = Path.Combine(
                root,
                file.Path.Replace('/', Path.DirectorySeparatorChar)
            );
            if (Matches(destination, file))
            {
                continue;
            }

            if (
                !archive.TryGetValue(file.Path, out PackageContentStager.Staged? source)
                || !string.Equals(source.Sha256, file.Sha256, StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    FormattableString.Invariant(
                        $"{label}: the recorded file '{file.Path}' is missing or changed, and the recorded version's archive does not carry those bytes, so this install cannot repair it — uninstall the package and install '{record.Package.Id}' {record.Package.Version} again."
                    )
                );
            }

            string? directory = Path.GetDirectoryName(destination);
            ScopePaths.RequireInside(root, destination, "write");
            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(source.Path, destination, overwrite: true);
            repaired.Add(destination);
        }

        return repaired;
    }

    /// <summary>Whether a file is there with the size and SHA-256 the record names.</summary>
    private static bool Matches(string path, InstalledFile file) =>
        File.Exists(path)
        && new FileInfo(path).Length == file.Size
        && string.Equals(Sha256(path), file.Sha256, StringComparison.Ordinal);

    /// <summary>
    /// The old install's bytes, kept until the new record commits. Every recorded file that exists is
    /// copied once; a file already gone has nothing to put back, and the record that names it still
    /// describes what the failure left behind.
    /// </summary>
    private sealed class PreviousContent
    {
        private readonly List<(string Destination, string Backup)> _files = [];

        private PreviousContent() { }

        /// <summary>Copies the old content into staging; null when there is nothing to keep.</summary>
        internal static PreviousContent? Capture(
            PackageInstallRecord record,
            PackageInstallTarget target,
            string stagingDirectory
        )
        {
            string root = ScopeRoot(record, target.StoreRoot);
            string backup = Path.Combine(stagingDirectory, "previous");
            PreviousContent previous = new();
            foreach (InstalledFile file in record.Content.Paths)
            {
                string source = Path.Combine(
                    root,
                    file.Path.Replace('/', Path.DirectorySeparatorChar)
                );
                if (!File.Exists(source))
                {
                    continue;
                }

                Directory.CreateDirectory(backup);
                string copy = Path.Combine(
                    backup,
                    previous._files.Count.ToString(CultureInfo.InvariantCulture)
                );
                File.Copy(source, copy, overwrite: true);
                previous._files.Add((source, copy));
            }

            return previous._files.Count == 0 ? null : previous;
        }

        /// <summary>Puts the old bytes back, recreating any directory the new run removed.</summary>
        internal void Restore()
        {
            foreach ((string destination, string backup) in _files)
            {
                try
                {
                    if (!File.Exists(backup))
                    {
                        continue;
                    }

                    if (Path.GetDirectoryName(destination) is { Length: > 0 } directory)
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.Copy(backup, destination, overwrite: true);
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                    // A file that cannot be put back is reported by the failure the caller sees;
                    // hiding the original reason behind a second one would help nobody.
                }
            }
        }
    }

    /// <summary>How deep a directory sits, for removing the deepest empty directory first.</summary>
    private static int Depth(string directory) =>
        directory.Count(character => character == Path.DirectorySeparatorChar);
}

using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// What a forced removal of a package whose record does not answer for itself did (spec:
/// "Uninstall"): the record path, the reason it could not be read, every path it removed, and the
/// places only a readable record could have named. There is no <see cref="PackageInstallRecord"/> to
/// carry, which is exactly why this is its own result.
/// </summary>
public sealed record PackageBrokenUninstallResult
{
    /// <summary>The package id the user named — the only identity an unreadable record leaves.</summary>
    public required string PackageId { get; init; }

    public required string RecordPath { get; init; }

    /// <summary>The one-line reason the record could not be believed.</summary>
    public required string Problem { get; init; }

    /// <summary>Absolute paths of every file removed, the record included.</summary>
    public IReadOnlyList<string> RemovedFiles { get; init; } = [];

    /// <summary>Absolute paths of every directory removed, deepest first.</summary>
    public IReadOnlyList<string> RemovedDirectories { get; init; } = [];

    /// <summary>
    /// The roots only a readable record could have searched: the scope's skill directory and the
    /// extensions root, where an extension id unlike the package id would have been named.
    /// </summary>
    public IReadOnlyList<string> UnknownPaths { get; init; } = [];
}

/// <summary>
/// The forced answer to a record that does not answer for itself (spec: "Uninstall", design D8):
/// a reinstall is the repair, and when the user would rather remove it, <c>--force</c> removes the
/// record's own directory and the extension directory that carries the package id — the only paths
/// the package id alone names. It prints every path it removed and names what a readable record
/// could have known: the skill directories, and an extension id that is not the package id.
/// <para>
/// It is a second entry point rather than a branch inside <see cref="Uninstall"/> because that
/// method's result carries the record it acted on, and there is no record here to carry. The plain
/// uninstall still refuses an unreadable record, <c>--force</c> or not: guessing at paths is this
/// method's job, and it says so.
/// </para>
/// </summary>
public sealed partial class PackageUninstaller
{
    public PackageBrokenUninstallResult UninstallBroken(PackageUninstallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Id);
        if (!request.Force)
        {
            throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"package '{request.Id}': removing content whose record cannot be read needs '--force', because the paths are the package id's guess rather than the record's list."
                )
            );
        }

        string store = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.StoreRoot));
        if (!Directory.Exists(store))
        {
            throw new InvalidOperationException(
                $"the state root '{store}' does not exist, so there is nothing installed to remove."
            );
        }

        string directory = Path.Combine(store, "packages", request.Id.ToLowerInvariant());
        string path = Path.Combine(directory, PackageInstallRecordJson.FileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"no installed package matches '{request.Id}' under '{store}' — an unreadable record at '<packages>/<package-id>/install.json' is what this removes."
                )
            );
        }

        PackageInstallRecord? record = PackageInstallRecordJson.TryReadForForce(
            path,
            out string? problem
        );
        if (record is not null)
        {
            throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"the record '{path}' answers for itself, so a plain uninstall removes exactly what it lists; this path exists for a record nothing can read."
                )
            );
        }

        List<string> files = [];
        List<string> directories = [];
        Remove(directory, files, directories);

        foreach (string extension in ExtensionCandidates(store, request.Id))
        {
            if (Directory.Exists(extension))
            {
                Remove(extension, files, directories);
            }
        }

        return new PackageBrokenUninstallResult
        {
            PackageId = request.Id,
            RecordPath = path,
            Problem = problem ?? "the record could not be read",
            RemovedFiles = files,
            RemovedDirectories = directories,
            UnknownPaths = [Path.Combine(store, "skills"), Path.Combine(store, "extensions")],
        };
    }

    /// <summary>
    /// The extension directories a package id alone names: the id as the user wrote it and its
    /// lowercase spelling, which is the record directory's. The extension's own id is not knowable
    /// from an unreadable record, and that is what the result says.
    /// </summary>
    private static IEnumerable<string> ExtensionCandidates(string store, string id)
    {
        string extensions = Path.Combine(store, "extensions");
        List<string> candidates =
        [
            Path.Combine(extensions, id),
            Path.Combine(extensions, id.ToLowerInvariant()),
        ];
        HashSet<string> seen = new(
            OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase
        );
        foreach (string candidate in candidates)
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>
    /// Deletes one directory tree and records every file and directory it removed, deepest first,
    /// so the command can print the paths instead of a count nobody can check.
    /// </summary>
    private static void Remove(string directory, List<string> files, List<string> directories)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        List<string> descendants;
        try
        {
            descendants =
            [
                .. Directory
                    .EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal),
            ];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"'{root}' could not be read, so nothing was removed from it: {exception.Message}"
                ),
                exception
            );
        }

        foreach (string entry in descendants)
        {
            if (File.Exists(entry))
            {
                File.Delete(entry);
                files.Add(entry);
            }
        }

        foreach (
            string entry in descendants
                .Where(Directory.Exists)
                .OrderByDescending(candidate => candidate.Length)
        )
        {
            Directory.Delete(entry);
            directories.Add(entry);
        }

        Directory.Delete(root);
        directories.Add(root);
    }
}

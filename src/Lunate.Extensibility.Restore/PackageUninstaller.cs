using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// One uninstall (spec: "Uninstall"): the package id or the extension id a record names, the state
/// root the records live under, and whether a changed file may be removed anyway.
/// </summary>
public sealed record PackageUninstallRequest
{
    /// <summary>A package id in any spelling, or an extension id exactly one record provides.</summary>
    public required string Id { get; init; }

    public required string StoreRoot { get; init; }

    /// <summary>Removes a changed recorded file instead of refusing, and acts on an edited record.</summary>
    public bool Force { get; init; }
}

/// <summary>
/// What an uninstall did: the record it acted on and where it was, the files it deleted, the files
/// it left because the package did not write them, the directories it kept for the same reason, and
/// whether the record itself is gone.
/// </summary>
public sealed record PackageUninstallResult
{
    public required PackageInstallRecord Record { get; init; }

    public required string RecordPath { get; init; }

    /// <summary>Absolute paths of the files the record named, as they were deleted.</summary>
    public IReadOnlyList<string> RemovedFiles { get; init; } = [];

    /// <summary>Absolute paths of files inside a directory the package created that it did not write.</summary>
    public IReadOnlyList<string> LeftFiles { get; init; } = [];

    /// <summary>Absolute paths of the directories kept because something else is in them.</summary>
    public IReadOnlyList<string> KeptDirectories { get; init; } = [];

    /// <summary>True when <c>--force</c> was needed: a changed file, or a record that stopped answering for itself.</summary>
    public bool Forced { get; init; }

    public bool RecordRemoved { get; init; }
}

/// <summary>
/// Removes one installed package (spec: "Uninstall"). It deletes exactly the paths the record lists,
/// in the scope root the record's scope and worktree imply, then the directories the install created
/// that are now empty, and finally the record — so a run that stops part-way leaves a record that
/// still describes what remains.
/// <para>
/// It refuses when a recorded file's hash no longer matches, naming the files, because those bytes
/// are the user's and not the package's; <c>--force</c> proceeds. A file in a directory the package
/// created but did not write is never deleted: it is left and named. The dependency lock and the
/// nupkgs stay — the lock is the record of a resolution, not installed content, and a reinstall
/// reuses it. Trust records are untouched: removing bytes does not revoke a decision about a path.
/// </para>
/// </summary>
public sealed partial class PackageUninstaller
{
    public PackageUninstallResult Uninstall(PackageUninstallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Id);

        string store = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.StoreRoot));
        if (!Directory.Exists(store))
        {
            throw new InvalidOperationException(
                $"the state root '{store}' does not exist, so there is nothing installed to remove."
            );
        }

        Found found = Resolve(store, request.Id, request.Force);
        PackageInstallRecord record = found.Record;
        string label = FormattableString.Invariant(
            $"package '{record.Package.Id}' {record.Package.Version}"
        );
        string scopeRoot = ScopeRoot(record, store);

        PackageContentState state = PackageContentVerifier.Verify(record, scopeRoot);
        if (state.Changed.Count > 0 && !request.Force)
        {
            throw new InvalidOperationException(
                $"{label}: uninstall refuses to delete "
                    + string.Join(
                        ", ",
                        state.Changed.Select(file => $"'{Resolve(scopeRoot, file)}'")
                    )
                    + " because its bytes no longer match the record. Nothing was deleted. Run "
                    + FormattableString.Invariant(
                        $"'lunate uninstall {record.Package.Id} --force' to remove it anyway."
                    )
            );
        }

        List<string> removed = [];
        foreach (InstalledFile file in record.Content.Paths)
        {
            string path = Resolve(scopeRoot, file.Path);
            if (!File.Exists(path))
            {
                // Already gone: the record describes less than the machine holds, and deleting what
                // is not there is not this command's problem to solve.
                continue;
            }

            File.Delete(path);
            removed.Add(path);
        }

        List<string> left = [];
        List<string> kept = [];
        foreach (string directory in ContentRoots(record, scopeRoot).OrderByDescending(Depth))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
                continue;
            }

            kept.Add(directory);
            left.AddRange(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories));
        }

        // The record last: a run that stopped before this still has a record describing what is
        // left, and a record that is gone means the content it named is gone.
        string recordDirectory = Path.GetDirectoryName(found.Path)!;
        string staging = Path.Combine(recordDirectory, "staging");
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        File.Delete(found.Path);
        TryDeleteEmpty(recordDirectory);

        return new PackageUninstallResult
        {
            Record = record,
            RecordPath = found.Path,
            RemovedFiles = removed,
            LeftFiles = [.. left.Order(StringComparer.Ordinal)],
            KeptDirectories = kept,
            Forced = request.Force && (state.Changed.Count > 0 || found.Modified),
            RecordRemoved = !File.Exists(found.Path),
        };
    }

    private static string Resolve(string scopeRoot, string relative) =>
        Path.Combine(scopeRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private static void TryDeleteEmpty(string directory)
    {
        try
        {
            if (
                Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any()
            )
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A directory that cannot be removed holds something else or is in use; an empty
            // leftover is not content and no command reads anything from it.
        }
    }
}

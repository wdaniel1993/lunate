namespace Lunate.Extensibility.Restore;

/// <summary>
/// The file side of an install (design D9): staged files are copied into the scope root through a
/// temporary file beside each destination, and a failure takes back exactly what the run wrote —
/// the files, the directories it created, the staging and an empty package directory. Nothing an
/// earlier install wrote is touched, because nothing here knows about it.
/// </summary>
public sealed partial class PackageInstaller
{
    /// <summary>
    /// Copies the staged files into the scope root, each through a temporary file beside its
    /// destination, so a reader never sees a half-written file. Every path is recorded as it lands:
    /// a failure part-way through knows exactly what to remove.
    /// </summary>
    private static void Commit(
        IReadOnlyList<PackageContentStager.Staged> staged,
        string scopeRoot,
        List<string> written,
        List<string> created
    )
    {
        foreach (PackageContentStager.Staged file in staged)
        {
            string destination = Path.Combine(
                scopeRoot,
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar)
            );
            string? directory = Path.GetDirectoryName(destination);
            if (directory is { Length: > 0 })
            {
                CreateDirectories(directory, created);
            }

            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(file.Path, temporary, overwrite: true);
                MoveIntoPlace(temporary, destination);
            }
            catch
            {
                // The half-copied temporary file is not content; it goes with the failure.
                TryDeleteFile(temporary);
                throw;
            }

            written.Add(destination);
        }
    }

    /// <summary>
    /// Replaces a destination that a running session may have loaded. Windows keeps a loaded
    /// assembly mapped until its load context is unloaded and collected, so the replace can fail
    /// for a moment while the previous owner lets go; it is retried briefly and then reported as
    /// "in use" — a fact the user can act on — instead of as an access-denied from the filesystem.
    /// </summary>
    private static void MoveIntoPlace(string temporary, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, destination, overwrite: true);
                return;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt < 3)
                {
                    Thread.Sleep(50);
                    continue;
                }

                throw new InvalidOperationException(
                    FormattableString.Invariant(
                        $"'{destination}' could not be replaced because another process — or the session that loaded it — has the file open. Stop that session and run the install again."
                    ),
                    exception
                );
            }
        }
    }

    /// <summary>Creates a directory chain and records the levels it created, deepest first.</summary>
    private static void CreateDirectories(string directory, List<string> created)
    {
        List<string> missing = [];
        string? current = directory;
        while (current is { Length: > 0 } && !Directory.Exists(current))
        {
            missing.Add(current);
            current = Path.GetDirectoryName(current);
        }

        Directory.CreateDirectory(directory);
        created.AddRange(missing);
    }

    /// <summary>
    /// Takes back what a failed install wrote: the files, the directories it created and the staging.
    /// An upgrade's rollback hands back the previous install's own bytes first, so a failure after
    /// the first destructive step leaves the install the record describes. Nothing else is touched —
    /// an installed package's content is not this path's to remove.
    /// </summary>
    private static void Rollback(
        IReadOnlyList<string> written,
        IReadOnlyList<string> created,
        PackageInstallTarget target,
        PreviousContent? previous
    )
    {
        foreach (string path in written)
        {
            TryDeleteFile(path);
        }

        previous?.Restore();

        foreach (string directory in created)
        {
            TryDeleteEmptyDirectory(directory);
        }

        Delete(target.StagingDirectory);
        TryDeleteEmptyDirectory(target.RecordDirectory);
    }

    private static void Delete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A staging directory that cannot be removed is left for the next run, which clears it
            // before extracting; failing the install over it would be worse than the leftover.
        }
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Windows holds a loaded assembly mapped until its load context is gone. Callers that
            // are replacing content must say so rather than leave the file behind silently.
            return false;
        }
    }

    private static void TryDeleteEmptyDirectory(string directory)
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
            // Someone else's file is in it, or the platform refuses; an empty leftover directory is
            // not content and no command reads anything from it.
        }
    }
}

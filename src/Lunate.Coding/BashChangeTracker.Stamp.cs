using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// How a scan answered: git saw the workspace, the stamp walk covered for git, or nothing could
/// look.
/// </summary>
internal enum ScanSource
{
    Git,
    Stamp,
    Unavailable,
}

/// <summary>
/// One scan's outcome: what changed and how it was seen. An empty change list with Git or Stamp
/// means "looked, nothing changed"; Unavailable means the workspace could not be looked at —
/// the two stayed indistinguishable until #418 and a caller reading a bare empty list could
/// not tell them apart.
/// </summary>
internal sealed record ScanReport(
    IReadOnlyList<BashChangeTracker.Change> Changes,
    ScanSource Source,
    string? FailureReason
);

/// <summary>
/// The content-stamp backstop of <see cref="BashChangeTracker"/>: when git cannot answer — a
/// broken HEAD, a starved runner, no git at all — the walk hashes every readable regular file
/// under the root and lets the usual snapshot diff answer from content, so a workspace a model
/// keeps editing stays observable (#418). Directories git's answer would never carry stay out:
/// build metadata and repository internals.
/// </summary>
internal sealed partial class BashChangeTracker
{
    /// <summary>
    /// Walks <paramref name="root"/> and hashes every readable regular file, skipping
    /// <c>.git</c>, <c>obj</c>, <c>bin</c> and <c>node_modules</c>. The kind comes from the
    /// previous snapshot (known path: modified; new path: created); without one the scan only
    /// baselines and the kind is inert. Null when the root itself cannot be walked, with the
    /// trouble named.
    /// </summary>
    private static (Scan? Scan, string? Trouble) StampScan(
        string root,
        Dictionary<string, string?>? previousStatus
    )
    {
        var entries = new List<(string Path, FileChangeKind Kind)>();
        var hashes = new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            Walk(root, relative: "");
        }
        catch (Exception exception)
            when (exception
                    is DirectoryNotFoundException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            return (null, $"{exception.GetType().Name}: {exception.Message}");
        }

        return (new Scan(Head: null, Prefix: "", entries, hashes), null);

        void Walk(string directory, string relative)
        {
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string name = Path.GetFileName(file);
                string path = relative.Length == 0 ? name : $"{relative}/{name}";
                string? hash = HashOf(root, path);
                if (hash is null)
                {
                    // A file that cannot be read must not publish, and an entry without a hash
                    // would fail the next content diff.
                    continue;
                }

                hashes[path] = hash;
                FileChangeKind kind =
                    previousStatus is not null && previousStatus.ContainsKey(path)
                        ? FileChangeKind.Modified
                        : FileChangeKind.Created;
                entries.Add((path, kind));
            }

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(child);
                if (name is ".git" or "obj" or "bin" or "node_modules")
                {
                    continue;
                }

                Walk(child, relative.Length == 0 ? name : $"{relative}/{name}");
            }
        }
    }
}

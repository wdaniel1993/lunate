namespace Lunate.Ai;

/// <summary>
/// Canonical absolute paths, with symlinks resolved for every existing component — the same rule the
/// workspace boundary applies (ADR-0016), moved down here so the state-root guard compares real
/// paths. Without it, <c>/var/…/worktree</c> and <c>/private/var/…/worktree</c> would be different
/// paths on macOS and a store inside the workspace would pass the containment check.
/// </summary>
internal static class PathCanonical
{
    private const int MaxLinkHops = 40;

    /// <summary>
    /// Resolves every existing symlink component of <paramref name="path"/> and returns the
    /// canonical absolute path. Dangling links resolve to their declared target, so a write through
    /// one cannot slip past a boundary check.
    /// </summary>
    internal static string Canonicalize(string path) => Canonicalize(path, depth: 0);

    private static string Canonicalize(string path, int depth)
    {
        if (depth > MaxLinkHops)
        {
            throw new IOException($"too many levels of symbolic links resolving '{path}'");
        }

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
        {
            return full;
        }

        var current = root;
        foreach (
            var segment in full[root.Length..]
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries
                )
        )
        {
            var next = Path.Combine(current, segment);
            var linkTarget = LinkTargetOf(next);
            current = linkTarget is null ? next : Canonicalize(linkTarget, depth + 1);
        }

        return current;
    }

    /// <summary>
    /// The link's raw target resolved against the link's own directory, or null when the component
    /// is not a link. Works for dangling links too — their target does not have to exist.
    /// </summary>
    private static string? LinkTargetOf(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        var target = info.LinkTarget;
        if (target is null)
        {
            return null;
        }

        return Path.IsPathRooted(target)
            ? target
            : Path.Combine(Path.GetDirectoryName(path)!, target);
    }
}

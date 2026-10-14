namespace Lunate.Agent;

/// <summary>
/// The canonicalization and repository-identity helpers shared by every workspace boundary
/// (ADR-0016): paths are canonicalized with symlinks resolved for existing components, and the
/// repository identity is read from the file system only — git is never executed. Unreadable or
/// malformed input yields nulls instead of throwing.
/// </summary>
public static class GitIdentity
{
    private const int MaxLinkHops = 40;

    /// <summary>
    /// Resolves every existing symlink component of <paramref name="path"/> and returns the
    /// canonical absolute path. Dangling links resolve to their declared target, so a write through
    /// one cannot slip past a boundary check.
    /// </summary>
    public static string Canonicalize(string path) => Canonicalize(path, depth: 0);

    /// <summary>
    /// Best-effort repository identity of a worktree root: a <c>.git</c> directory is the common
    /// dir of a main checkout; a <c>.git</c> file points at a linked worktree's gitdir, whose
    /// <c>commondir</c> file names the shared git directory. Worktrees of one repository share
    /// <c>GitCommonDir</c>, which is what makes them the same repository. Returns (null, null)
    /// outside a repository and for unreadable or malformed input.
    /// </summary>
    public static (string? RepoRoot, string? GitCommonDir) Detect(string worktreeRoot)
    {
        try
        {
            var dotGit = Path.Combine(worktreeRoot, ".git");
            if (Directory.Exists(dotGit))
            {
                return (worktreeRoot, Canonicalize(dotGit));
            }

            if (!File.Exists(dotGit))
            {
                return (null, null);
            }

            var gitDir = ReadGitDirPointer(dotGit);
            if (gitDir is null)
            {
                return (null, null);
            }

            var commonDir = ReadCommonDir(gitDir);
            if (commonDir is null)
            {
                return (null, null);
            }

            var repoRoot =
                Path.GetFileName(commonDir) == ".git" ? Path.GetDirectoryName(commonDir) : null;
            return (repoRoot, commonDir);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return (null, null);
        }
    }

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

    private static string? ReadGitDirPointer(string dotGitPath)
    {
        var line = File.ReadLines(dotGitPath).FirstOrDefault()?.Trim();
        if (line is null || !line.StartsWith("gitdir:", StringComparison.Ordinal))
        {
            return null;
        }

        var target = line["gitdir:".Length..].Trim();
        if (target.Length == 0)
        {
            return null;
        }

        var resolved = Path.IsPathRooted(target)
            ? target
            : Path.Combine(Path.GetDirectoryName(dotGitPath)!, target);
        return Canonicalize(resolved);
    }

    private static string? ReadCommonDir(string gitDir)
    {
        var line = File.ReadLines(Path.Combine(gitDir, "commondir")).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        return Canonicalize(Path.Combine(gitDir, line));
    }
}

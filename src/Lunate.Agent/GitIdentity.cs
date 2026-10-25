using Lunate.Ai;

namespace Lunate.Agent;

/// <summary>
/// The canonicalization and repository-identity helpers shared by every workspace boundary
/// (ADR-0016): paths are canonicalized with symlinks resolved for existing components, and the
/// repository identity is read from the file system only — git is never executed. Unreadable or
/// malformed input yields nulls instead of throwing.
/// </summary>
public static class GitIdentity
{
    /// <summary>
    /// Resolves every existing symlink component of <paramref name="path"/> and returns the
    /// canonical absolute path. Dangling links resolve to their declared target, so a write through
    /// one cannot slip past a boundary check.
    /// </summary>
    public static string Canonicalize(string path) => PathCanonical.Canonicalize(path);

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

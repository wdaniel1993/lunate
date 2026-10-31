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
    /// Best-effort repository identity of a directory: the nearest ancestor that carries a
    /// <c>.git</c> entry, so a session launched from <c>/repo/src/App</c> is the same repository as
    /// one launched from <c>/repo</c>. A <c>.git</c> directory is the common dir of a main checkout;
    /// a <c>.git</c> file points at a linked worktree's gitdir, whose <c>commondir</c> file names the
    /// shared git directory. Worktrees of one repository share <c>GitCommonDir</c>, which is what
    /// makes them the same repository. Returns (null, null) outside a repository and for unreadable
    /// or malformed input.
    /// </summary>
    public static (string? RepoRoot, string? GitCommonDir) Detect(string worktreeRoot)
    {
        try
        {
            for (
                string? directory = Canonicalize(worktreeRoot);
                directory is not null;
                directory = Path.GetDirectoryName(directory)
            )
            {
                var dotGit = Path.Combine(directory, ".git");
                if (Directory.Exists(dotGit))
                {
                    return (directory, Canonicalize(dotGit));
                }

                if (File.Exists(dotGit))
                {
                    return FromGitFile(dotGit);
                }
            }

            return (null, null);
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

    /// <summary>
    /// The identity a linked worktree's <c>.git</c> file describes: the gitdir it names, and the
    /// common dir that gitdir's <c>commondir</c> file names. The repository root is the parent of a
    /// common dir called <c>.git</c>; a common dir that is not (a bare repository or a submodule's)
    /// has no worktree root to report.
    /// </summary>
    private static (string? RepoRoot, string? GitCommonDir) FromGitFile(string dotGitPath)
    {
        var gitDir = ReadGitDirPointer(dotGitPath);
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

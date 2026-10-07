namespace Lunate.Coding;

/// <summary>
/// The file-tool boundary: every path is canonicalized (symlinks resolved for existing components)
/// and must end up equal to an allowed root or under it. The policy is pinned by ADR-0016: the
/// check runs at call time and is a guard rail, not a sandbox.
/// </summary>
public sealed class Workspace
{
    private readonly string _workingDirectory;
    private readonly IReadOnlyList<string> _roots;
    private readonly StringComparison _comparison;

    /// <summary>The canonical working directory of this run: the boundary's first allowed root.</summary>
    public string WorktreeRoot { get; }

    /// <summary>The main worktree's root from the file system, or null outside a repository.</summary>
    public string? RepoRoot { get; }

    /// <summary>The shared git directory from the file system, or null outside a repository.</summary>
    public string? GitCommonDir { get; }

    public Workspace(string workingDirectory, IReadOnlyList<string>? extraRoots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        _workingDirectory = Canonicalize(workingDirectory);
        WorktreeRoot = _workingDirectory;
        (RepoRoot, GitCommonDir) = DetectIdentity(WorktreeRoot);
        var roots = new List<string> { _workingDirectory };
        if (extraRoots is not null)
        {
            foreach (var root in extraRoots)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(root);
                roots.Add(Canonicalize(root));
            }
        }

        _roots = roots;
        _comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
    }

    /// <summary>
    /// Resolves <paramref name="path"/> against the working directory and accepts it only when its
    /// canonical target is inside an allowed root. On refusal <paramref name="error"/> names the
    /// path, the resolved target and the allowed roots.
    /// </summary>
    public bool TryResolve(string path, out ResolvedPath resolved, out string error)
    {
        resolved = null!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "path must not be empty";
            return false;
        }

        string canonical;
        try
        {
            canonical = Canonicalize(Path.Combine(_workingDirectory, path));
        }
        catch (ArgumentException)
        {
            error = $"path '{path}' is not a valid path";
            return false;
        }
        catch (IOException exception)
        {
            error = $"path '{path}' could not be resolved: {exception.Message}";
            return false;
        }

        var root = FindRoot(canonical);
        if (root is null)
        {
            error =
                $"path '{path}' is outside the workspace "
                + $"(resolves to '{canonical}'; allowed roots: {string.Join(", ", _roots)})";
            return false;
        }

        var relative = Path.GetRelativePath(root, canonical)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        resolved = new ResolvedPath(canonical, relative);
        return true;
    }

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
    /// is not a link. Works for dangling links too — their target does not have to exist, so a
    /// write through a dangling link cannot slip past the boundary.
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

    private const int MaxLinkHops = 40;

    /// <summary>
    /// Best-effort repository identity from the file system only (git is never executed): a
    /// <c>.git</c> directory is the common dir of a main checkout; a <c>.git</c> file points at a
    /// linked worktree's gitdir, whose <c>commondir</c> file names the shared git directory.
    /// Unreadable or malformed input yields nulls instead of throwing.
    /// </summary>
    private static (string? RepoRoot, string? GitCommonDir) DetectIdentity(string worktreeRoot)
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

    private string? FindRoot(string canonical)
    {
        foreach (var root in _roots)
        {
            if (canonical.Equals(root, _comparison))
            {
                return root;
            }

            var prefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (canonical.StartsWith(prefix, _comparison))
            {
                return root;
            }
        }

        return null;
    }
}

/// <summary>The canonical absolute path and the workspace-relative display form (forward slashes).</summary>
public sealed record ResolvedPath(string AbsolutePath, string RelativePath);

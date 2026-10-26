using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

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

    /// <summary>The canonical working directory of this run: the boundary's first allowed root.</summary>
    public string WorktreeRoot { get; }

    /// <summary>The main worktree's root from the file system, or null outside a repository.</summary>
    public string? RepoRoot { get; }

    /// <summary>The shared git directory from the file system, or null outside a repository.</summary>
    public string? GitCommonDir { get; }

    /// <summary>The identity extensions receive through the <c>core/workspace</c> handle.</summary>
    public WorkspaceInfo Identity => new(WorktreeRoot, RepoRoot, GitCommonDir);

    public Workspace(string workingDirectory, IReadOnlyList<string>? extraRoots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        _workingDirectory = Canonicalize(workingDirectory);
        WorktreeRoot = _workingDirectory;
        (RepoRoot, GitCommonDir) = GitIdentity.Detect(WorktreeRoot);
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

    internal static string Canonicalize(string path) => GitIdentity.Canonicalize(path);

    /// <summary>
    /// True when the canonical <paramref name="path"/> equals <paramref name="root"/> or lies under
    /// it, under the platform's case rule. A trailing separator on the root is tolerated; both
    /// arguments must already be canonical, so a symlink cannot make an outsider look like a child.
    /// </summary>
    internal static bool IsInside(string root, string path)
    {
        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        if (path.Equals(root, comparison))
        {
            return true;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison);
    }

    private string? FindRoot(string canonical)
    {
        foreach (var root in _roots)
        {
            if (IsInside(root, canonical))
            {
                return root;
            }
        }

        return null;
    }
}

/// <summary>The canonical absolute path and the workspace-relative display form (forward slashes).</summary>
public sealed record ResolvedPath(string AbsolutePath, string RelativePath);

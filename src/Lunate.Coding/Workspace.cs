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

    public Workspace(string workingDirectory, IReadOnlyList<string>? extraRoots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        _workingDirectory = Canonicalize(workingDirectory);
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

    internal static string Canonicalize(string path)
    {
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
            var link = ResolveLink(next);
            current = link is null ? next : Canonicalize(link);
        }

        return current;
    }

    private static string? ResolveLink(string path)
    {
        FileSystemInfo info;
        if (Directory.Exists(path))
        {
            info = new DirectoryInfo(path);
        }
        else if (File.Exists(path))
        {
            info = new FileInfo(path);
        }
        else
        {
            return null;
        }

        if (info.LinkTarget is null)
        {
            return null;
        }

        return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
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

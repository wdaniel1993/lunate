namespace Lunate.Coding;

/// <summary>
/// The per-run read-only allowance of the read tool: the discovered skill directories. The
/// session's own tools share one allowance instance (a wholesale reload refreshes it in place);
/// child runs in another worktree get a fresh allowance computed from their own discovery.
/// </summary>
internal sealed class SkillReadAllowance
{
    private volatile IReadOnlyList<string> _roots;

    internal SkillReadAllowance(IReadOnlyList<string>? roots = null) => _roots = Canonical(roots);

    /// <summary>The current allowance roots, all canonicalized.</summary>
    internal IReadOnlyList<string> Roots => _roots;

    /// <summary>Replaces the allowance (a session reload re-scans its skills).</summary>
    internal void Set(IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        _roots = Canonical(roots);
    }

    /// <summary>Whether a canonical path equals an allowed root or lies under one.</summary>
    internal bool Contains(string canonicalPath)
    {
        ArgumentNullException.ThrowIfNull(canonicalPath);
        StringComparison comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        foreach (string root in _roots)
        {
            if (IsWithinRoot(canonicalPath, root, comparison))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWithinRoot(string canonicalPath, string root, StringComparison comparison)
    {
        if (canonicalPath.Equals(root, comparison))
        {
            return true;
        }

        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return canonicalPath.StartsWith(prefix, comparison);
    }

    private static IReadOnlyList<string> Canonical(IReadOnlyList<string>? roots) =>
        roots is null ? [] : [.. roots.Select(Workspace.Canonicalize)];
}

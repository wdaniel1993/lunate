namespace Lunate.Coding;

/// <summary>Path rendering shared by the index and the invocation wrapper.</summary>
internal static class SkillPaths
{
    /// <summary>
    /// The display form of a skill path: relative to the workspace when inside it, otherwise the
    /// canonical absolute path (directly usable by the read tool). Always forward slashes.
    /// </summary>
    internal static string Display(string path, string worktreeRoot)
    {
        string canonical = Workspace.Canonicalize(path);
        string root = Workspace.Canonicalize(worktreeRoot);
        if (IsInside(canonical, root))
        {
            return Forward(Path.GetRelativePath(root, canonical));
        }

        return Forward(canonical);
    }

    private static string Forward(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static bool IsInside(string canonical, string root)
    {
        StringComparison comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        if (canonical.Equals(root, comparison))
        {
            return true;
        }

        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return canonical.StartsWith(prefix, comparison);
    }
}

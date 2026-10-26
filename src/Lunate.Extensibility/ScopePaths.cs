using Lunate.Ai;

namespace Lunate.Extensibility;

/// <summary>
/// The real-path confinement of a destination inside a scope root (design D11). The record's paths
/// are checked lexically when it is parsed, but a directory the path walks through can still be a
/// symlink that leaves the root: lexical confinement says nothing about where the bytes land. Every
/// write, repair with delete, and uninstall asks this before it touches the file system, so a
/// symlinked ancestor is refused by name instead of redirecting the operation outside the scope.
/// </summary>
internal static class ScopePaths
{
    private static readonly StringComparison Comparison = OperatingSystem.IsLinux()
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Returns <paramref name="path"/>'s real path and refuses when it is not under
    /// <paramref name="scopeRoot"/> once every existing component is resolved through its symlinks.
    /// <paramref name="action"/> words the refusal ("write", "delete", "load"); the message names the
    /// offending path, because that is what the user has to look at.
    /// </summary>
    internal static string RequireInside(string scopeRoot, string path, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scopeRoot));
        string full = Path.GetFullPath(path);
        string real = Path.TrimEndingDirectorySeparator(PathCanonical.Canonicalize(full));
        string canonicalRoot = Path.TrimEndingDirectorySeparator(PathCanonical.Canonicalize(root));
        if (IsUnder(real, canonicalRoot))
        {
            return real;
        }

        string reason = HasSymbolicLink(root, full)
            ? $"the scope path '{path}' is a symbolic link that leads outside the scope root"
            : $"the path '{path}' leads outside the scope root '{scopeRoot}'";
        throw new InvalidOperationException($"{reason}; refusing to {action} there.");
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="root"/> or sits under it.</summary>
    private static bool IsUnder(string path, string root) =>
        string.Equals(path, root, Comparison)
        || path.StartsWith(
            Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            Comparison
        );

    /// <summary>
    /// Whether any component of <paramref name="path"/> at or below <paramref name="scopeRoot"/> is a
    /// symbolic link. The root's own spelling is deliberately not judged: a store under a symlinked
    /// temporary directory resolves differently without any link inside the store being at fault.
    /// </summary>
    private static bool HasSymbolicLink(string scopeRoot, string path)
    {
        string relative;
        try
        {
            relative = Path.GetRelativePath(scopeRoot, path);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (
            relative.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relative)
            || relative == "."
        )
        {
            return false;
        }

        string current = scopeRoot;
        foreach (
            string part in relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            current = Path.Combine(current, part);
            try
            {
                FileSystemInfo info = Directory.Exists(current)
                    ? new DirectoryInfo(current)
                    : new FileInfo(current);
                if (info.LinkTarget is not null)
                {
                    return true;
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // A component this platform cannot inspect is not evidence of a link; the real-path
                // comparison above already decided that the destination leaves the root.
            }
        }

        return false;
    }
}

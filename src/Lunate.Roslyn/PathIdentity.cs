namespace Lunate.Roslyn;

internal static class PathIdentity
{
    private const int MaxLinkHops = 40;

    /// <summary>Case sensitivity of path comparison, per platform (the repo-wide rule).</summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static StringComparer Comparer { get; } =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>Resolves symlinks for every existing component; missing components stay as-is.</summary>
    public static string Canonicalize(string path) => Canonicalize(path, depth: 0);

    /// <summary>True when <paramref name="path"/> equals or sits under <paramref name="root"/>.</summary>
    public static bool IsUnder(string root, string path)
    {
        if (path.Equals(root, Comparison))
        {
            return true;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, Comparison);
    }

    /// <summary>
    /// The model-facing form of <paramref name="path"/>: relative to the root with forward slashes
    /// when under it, unchanged otherwise.
    /// </summary>
    public static string RelativeOrAbsolute(string root, string path)
    {
        string canonicalRoot;
        string canonicalPath;
        try
        {
            canonicalRoot = Canonicalize(root);
            canonicalPath = Canonicalize(path);
        }
        catch (Exception exception)
            when (exception is IOException or ArgumentException or NotSupportedException)
        {
            return path;
        }

        if (!IsUnder(canonicalRoot, canonicalPath))
        {
            return path;
        }

        return Path.GetRelativePath(canonicalRoot, canonicalPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
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

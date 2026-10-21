namespace Lunate.Coding;

/// <summary>The one path-scope rule the optional search tools share.</summary>
internal static class SearchScope
{
    /// <summary>Whether <paramref name="path"/> lies at or under the resolved search root.</summary>
    internal static bool Contains(string relativeRoot, string path)
    {
        // The file index yields platform separators and callers may pass either, so the
        // rule compares slash-normalized forms and never assumes the host's separator.
        string root = Normalize(relativeRoot);
        string candidate = Normalize(path);
        return root is "." or ""
            || string.Equals(candidate, root, StringComparison.Ordinal)
            || candidate.StartsWith(root + "/", StringComparison.Ordinal);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}

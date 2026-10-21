namespace Lunate.Coding;

/// <summary>The one path-scope rule the optional search tools share.</summary>
internal static class SearchScope
{
    /// <summary>Whether <paramref name="path"/> lies at or under the resolved search root.</summary>
    internal static bool Contains(string relativeRoot, string path) =>
        relativeRoot is "." or ""
        || string.Equals(path, relativeRoot, StringComparison.Ordinal)
        || path.StartsWith(relativeRoot + "/", StringComparison.Ordinal);
}

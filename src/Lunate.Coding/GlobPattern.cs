using System.Text.RegularExpressions;

namespace Lunate.Coding;

/// <summary>
/// One glob matched against a forward-slash workspace-relative path, with the same translation the
/// <c>.gitignore</c> engine uses. A pattern without <c>/</c> matches the basename at any depth; a
/// pattern with <c>/</c> (or a leading <c>/</c>) is anchored to the search root. Case follows the
/// workspace rule: sensitive on Linux, insensitive elsewhere.
/// </summary>
internal sealed class GlobPattern
{
    private readonly Regex _matcher;

    private GlobPattern(Regex matcher) => _matcher = matcher;

    internal static GlobPattern Parse(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        string value = pattern.Replace('\\', '/');
        bool anchored = value.StartsWith('/');
        if (anchored)
        {
            value = value[1..];
        }
        else
        {
            anchored = value.Contains('/');
        }

        return new GlobPattern(
            GitIgnore.Translate(value, anchored, ignoreCase: !OperatingSystem.IsLinux())
        );
    }

    internal bool IsMatch(string relativePath) => _matcher.IsMatch(relativePath);
}

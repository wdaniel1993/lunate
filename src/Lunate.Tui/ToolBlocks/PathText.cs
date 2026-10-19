using System.Globalization;

namespace Lunate.Tui;

/// <summary>
/// Pure path shortening for compact display: the home directory becomes <c>~</c>, then the middle
/// of the path elides so the basename survives and the result never exceeds the budget
/// (<c>~/dev/l…/Session.cs</c>). Short paths are returned unchanged; both separator styles elide.
/// </summary>
internal static class PathText
{
    /// <summary>The header summaries' path budget; the viewer derives its budget from the width.</summary>
    public const int HeaderBudget = 40;

    private const string Ellipsis = "\u2026";

    /// <summary>The user profile, the default home for display shortening.</summary>
    public static string Home() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Shorten(string? path, int budget, string? home = null)
    {
        if (string.IsNullOrEmpty(path) || budget < 1)
        {
            return string.Empty;
        }

        string text = Tilde(path, home);
        return text.Length <= budget ? text : Elide(text, budget);
    }

    private static string Tilde(string path, string? home)
    {
        if (string.IsNullOrEmpty(home) || path.Length < home.Length)
        {
            return path;
        }

        if (path.Equals(home, StringComparison.Ordinal))
        {
            return "~";
        }

        return path.StartsWith(home, StringComparison.Ordinal) && IsSeparator(path[home.Length])
            ? "~" + path[home.Length..]
            : path;
    }

    private static string Elide(string text, int budget)
    {
        int separator = text.LastIndexOfAny(['/', '\\']);
        int head = separator < 0 ? -1 : budget - 1 - (text.Length - separator);
        return head > 0
            ? string.Concat(text.AsSpan(0, head), Ellipsis, text.AsSpan(separator))
            : Ellipsis + Tail(text, budget - 1);
    }

    private static string Tail(string text, int length) =>
        length <= 0 ? string.Empty
        : length < text.Length ? text[^length..]
        : text;

    private static bool IsSeparator(char value) => value is '/' or '\\';
}

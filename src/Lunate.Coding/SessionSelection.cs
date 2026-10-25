using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// The launch-time session choice shared by the interactive and print frontends: path detection,
/// id lookup across the store and the newest-by-cwd query. The caller loads the chosen file and
/// owns how a failure is presented.
/// </summary>
internal static class SessionSelection
{
    /// <summary>The user profile's session store.</summary>
    internal static string DefaultRoot => Path.Combine(Lunate.Ai.LunateHome.Store, "sessions");

    /// <summary>The workspace path comparison: ordinal on Linux, ordinal-ignore-case elsewhere.</summary>
    internal static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>A separator or the <c>.jsonl</c> suffix marks a path; any other value is an id.</summary>
    internal static bool IsPath(string value) =>
        value.Contains('/')
        || value.Contains('\\')
        || value.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);

    /// <summary><c>--session &lt;value&gt;</c>: the path, or an error naming the input.</summary>
    internal static SessionChoice FromValue(string sessionsRoot, string value)
    {
        if (IsPath(value))
        {
            return new SessionChoice(value, value, null);
        }

        IReadOnlyList<SessionSummary> matches = Session.FindById(sessionsRoot, value);
        if (matches.Count == 0)
        {
            return new SessionChoice(
                null,
                value,
                $"no session with id '{value}'; run a prompt to start one"
            );
        }

        if (matches.Count > 1)
        {
            return new SessionChoice(
                null,
                value,
                $"session id '{value}' is ambiguous: {string.Join(", ", matches.Select(match => match.Path))}"
            );
        }

        return new SessionChoice(matches[0].Path, value, null);
    }

    /// <summary>
    /// <c>--continue</c>: the newest session whose header recorded <paramref name="cwd"/>, or an
    /// error naming the directory.
    /// </summary>
    internal static SessionChoice Continue(string sessionsRoot, string cwd) =>
        Session.FindNewest(sessionsRoot, cwd, PathComparison) is { } newest
            ? new SessionChoice(newest.Path, newest.Id, null)
            : new SessionChoice(null, cwd, $"no session for '{cwd}'; run a prompt to start one");
}

/// <summary>A resolved session file and the input label, or the error to report instead.</summary>
internal sealed record SessionChoice(string? Path, string Label, string? Error);

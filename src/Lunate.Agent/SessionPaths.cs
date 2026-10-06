using System.Security.Cryptography;
using System.Text;

namespace Lunate.Agent;

/// <summary>Sessions live under ~/.lunate/sessions/keyed by the project working directory.</summary>
public static class SessionPaths
{
    /// <summary>The sessions directory for a project working directory.</summary>
    public static string ForProject(string cwd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cwd);
        string hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(cwd)))
        )[..8];
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lunate",
            "sessions",
            hash
        );
    }

    /// <summary>The session file name for a session id.</summary>
    public static string SessionFileName(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (
            sessionId.Contains('/')
            || sessionId.Contains('\\')
            || sessionId.Contains("..", StringComparison.Ordinal)
        )
        {
            throw new ArgumentException(
                $"Session id '{sessionId}' must not contain path separators or '..'.",
                nameof(sessionId)
            );
        }

        return sessionId + ".jsonl";
    }
}

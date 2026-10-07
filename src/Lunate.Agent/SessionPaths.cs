using System.Security.Cryptography;
using System.Text;

namespace Lunate.Agent;

/// <summary>
/// Sessions live under ~/.lunate/sessions: grouped by repository identity with one folder per
/// worktree, or by project working directory outside a repository.
/// </summary>
public static class SessionPaths
{
    /// <summary>The sessions directory for a project working directory.</summary>
    public static string ForProject(string cwd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cwd);
        return System.IO.Path.Combine(SessionsRoot, Hash8(System.IO.Path.GetFullPath(cwd)));
    }

    /// <summary>
    /// The sessions directory for a repository worktree: one folder per repository identity and
    /// one per worktree path beneath it.
    /// </summary>
    public static string ForRepository(string repoIdentity, string worktreePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        return System.IO.Path.Combine(
            SessionsRoot,
            Hash8(repoIdentity),
            Hash8(System.IO.Path.GetFullPath(worktreePath))
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

    private static string SessionsRoot =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lunate",
            "sessions"
        );

    private static string Hash8(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8];
}

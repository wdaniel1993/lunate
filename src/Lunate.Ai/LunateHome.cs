namespace Lunate.Ai;

/// <summary>
/// Lunate's state root — <c>~/.lunate</c> on every platform, or <c>LUNATE_HOME</c> when that names an
/// absolute path. A process that must not touch the user's own state (a test, a portable install, a
/// sandboxed run) points this at a directory instead of trying to rewrite the user profile:
/// Windows resolves the profile through the shell, so <c>HOME</c> alone does not move it.
/// </summary>
/// <remarks>
/// It lives in <c>Lunate.Ai</c> because that is the lowest assembly every state consumer already
/// references — the agent, the extensions host and its restorer — so one rule serves all of them
/// instead of a copy per project.
/// <para>
/// The variable is read from the process environment and nowhere else: no <c>.env</c> file, settings
/// file, <c>AGENTS.md</c> or project file may supply it, because a repository that could choose the
/// store could choose the one its own trust records live in.
/// </para>
/// </remarks>
internal static class LunateHome
{
    public const string Variable = "LUNATE_HOME";

    /// <summary>
    /// The refusal a caller with a workspace reports when the configured root would put the trust
    /// store — and sessions, credentials and settings — inside the tree it governs. A repository can
    /// write anything under itself, so records there answer to the repository, not to the user.
    /// </summary>
    internal const string InsideWorkspaceRefusal =
        "LUNATE_HOME points inside the workspace; trust records there could be written by the repository";

    /// <summary>The directory that holds <c>sessions</c>, <c>extensions</c>, <c>cache</c>, the trust
    /// store, the model catalog and recordings.</summary>
    public static string Store => Resolve(Environment.GetEnvironmentVariable(Variable), Profile);

    /// <summary>
    /// The user's real profile directory. Only the conventions shared with other agents
    /// (<c>~/.agents</c>) and presentation read it; Lunate's own state goes through
    /// <see cref="Store"/>.
    /// </summary>
    internal static string Profile =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Whether the process was pointed at a root of its own.</summary>
    internal static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable));

    /// <summary>
    /// The active home as both user-facing surfaces spell it, or null when the variable is unset.
    /// One property so <c>lunate --version</c> and the welcome header cannot drift apart.
    /// </summary>
    internal static string? ActiveHomeLine =>
        IsConfigured ? string.Concat("home ", Store, " (", Variable, ")") : null;

    /// <summary>
    /// The store for a process that knows its workspace: the same rule as <see cref="Store"/>, plus
    /// the refusal when <c>LUNATE_HOME</c> lands inside the worktree or the repository it belongs to.
    /// An unset variable is never refused — the default <c>~/.lunate</c> is the user's own state even
    /// when the session runs from the home directory.
    /// </summary>
    internal static string StoreOutsideWorkspace(params string?[] roots)
    {
        string store = Store;
        if (IsConfigured)
        {
            EnsureOutsideWorkspace(store, roots);
        }

        return store;
    }

    /// <summary>The refusal itself, separated so a test can state it without moving the environment.</summary>
    internal static void EnsureOutsideWorkspace(string store, params string?[] roots)
    {
        foreach (string? root in roots)
        {
            if (!string.IsNullOrWhiteSpace(root) && IsInside(store, root))
            {
                throw new InvalidOperationException(InsideWorkspaceRefusal);
            }
        }
    }

    /// <summary>
    /// Real containment: both paths are made absolute, <c>.</c> and <c>..</c> are resolved, existing
    /// symlink components are resolved too (so <c>/var/…</c> and <c>/private/var/…</c> agree on
    /// macOS), a trailing separator is ignored, the comparison follows the platform's case rule, and
    /// a path equal to the root counts as inside.
    /// </summary>
    internal static bool IsInside(string path, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        string candidate = Canonical(path);
        string boundary = Canonical(root);
        if (string.Equals(candidate, boundary, PathComparison))
        {
            return true;
        }

        // A filesystem root already ends in its separator, so appending another would never match.
        string prefix = Path.EndsInDirectorySeparator(boundary)
            ? boundary
            : boundary + Path.DirectorySeparatorChar;
        if (candidate.StartsWith(prefix, PathComparison))
        {
            return true;
        }

        return Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar
            && candidate.StartsWith(boundary + Path.AltDirectorySeparatorChar, PathComparison);
    }

    /// <summary>
    /// The store for a caller that already holds a profile directory — a session option in a test, a
    /// sandbox — so an explicit profile still wins over the environment, and the environment wins
    /// over the real profile.
    /// </summary>
    internal static string In(string? profile)
    {
        return string.IsNullOrWhiteSpace(profile) ? Store : Path.Combine(profile, ".lunate");
    }

    /// <summary>The rule itself, so a test can state it without moving the process's environment.</summary>
    internal static string Resolve(string? configured, string profile)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(profile, ".lunate");
        }

        if (!Path.IsPathFullyQualified(configured))
        {
            throw new InvalidOperationException(
                $"{Variable} must be an absolute path; '{configured}' is relative."
            );
        }

        return Path.TrimEndingDirectorySeparator(configured);
    }

    private static string Canonical(string path) =>
        Path.TrimEndingDirectorySeparator(PathCanonical.Canonicalize(path));

    /// <summary>Windows and macOS compare case-insensitively by default; Linux does not.</summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}

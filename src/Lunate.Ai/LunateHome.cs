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
/// </remarks>
internal static class LunateHome
{
    public const string Variable = "LUNATE_HOME";

    /// <summary>The directory that holds <c>sessions</c>, <c>extensions</c>, <c>cache</c>, the trust
    /// store, the model catalog and recordings.</summary>
    public static string Store =>
        Resolve(
            Environment.GetEnvironmentVariable(Variable),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        );

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
}

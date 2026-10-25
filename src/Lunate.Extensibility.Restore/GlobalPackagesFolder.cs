namespace Lunate.Extensibility.Restore;

/// <summary>
/// Where restored packages go. NuGet's own precedence, in one testable function: an explicit
/// override wins, then <c>globalPackagesFolder</c> from configuration, then <c>NUGET_PACKAGES</c>,
/// then <c>~/.nuget/packages</c>. Never a Lunate-owned folder — the cache is content-addressed and
/// shared with the user's SDK, so two restores of the same package are one copy.
/// </summary>
public static class GlobalPackagesFolder
{
    /// <summary>The folder to restore into; every value is normalized to an absolute path.</summary>
    public static string Resolve(string? configured, string? environment, string userProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfile);

        string? chosen = First(configured, environment);
        return Path.GetFullPath(chosen ?? Path.Combine(userProfile, ".nuget", "packages"));
    }

    private static string? First(string? configured, string? environment) =>
        configured is { Length: > 0 } && !string.IsNullOrWhiteSpace(configured) ? configured
        : environment is { Length: > 0 } && !string.IsNullOrWhiteSpace(environment) ? environment
        : null;
}

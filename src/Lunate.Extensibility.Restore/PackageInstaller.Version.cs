using NuGet.Versioning;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// What an install installs (design D1): the exact version it names, or the latest stable one the
/// feeds serve, resolved once and then recorded exactly. A range, a wildcard or a float is a usage
/// error here — a range belongs to a dependency's declaration, not to a user's install, and "which
/// version am I running" must have one answer.
/// </summary>
internal sealed partial class PackageInstaller
{
    /// <summary>
    /// The exact version, or null when the request names none and the latest stable version is
    /// resolved from the feeds (design D1). A range or a wildcard is refused here, before any feed
    /// is asked.
    /// </summary>
    private static NuGetVersion? TryExactVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        if (
            version.Contains(',', StringComparison.Ordinal)
            || version.Contains('*', StringComparison.Ordinal)
            || !NuGetVersion.TryParse(version, out NuGetVersion? parsed)
        )
        {
            throw new ArgumentException(
                $"'{version}' is not an exact version; install an exact version such as 1.4.2 — a "
                    + "range or a wildcard belongs to a dependency's declaration, not to an install.",
                nameof(version)
            );
        }

        return parsed;
    }

    /// <summary>
    /// The latest stable version the feeds serve, resolved once and recorded exactly (design D1):
    /// a bare install never re-resolves, and a package no feed serves names the id and the feeds.
    /// </summary>
    private static async Task<NuGetVersion> LatestStableAsync(
        NuGetPackageFeed feed,
        string id,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<NuGetVersion> versions = await feed.VersionsAsync(id, cancellationToken)
            .ConfigureAwait(false);
        NuGetVersion? latest = versions
            .Where(version => !version.IsPrerelease)
            .OrderByDescending(version => version)
            .FirstOrDefault();
        if (latest is null)
        {
            throw new InvalidOperationException(
                $"package '{id}' publishes no stable version on {string.Join(", ", feed.Names)}; "
                    + "name an exact prerelease version to install one."
            );
        }

        return latest;
    }
}

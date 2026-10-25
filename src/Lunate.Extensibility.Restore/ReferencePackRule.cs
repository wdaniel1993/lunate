using Lunate.Extensibility;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// How the reference pack's version is chosen, recorded in the lock so a later restore resolves to
/// the same pack. The exact runtime version when the feed publishes it, otherwise the lowest
/// published pack of the same major/minor at or above the runtime (a newer pack is a superset of the
/// API the host runs on), and as a last resort the highest published pack below it, when the runtime
/// is ahead of the feed — a staged patch whose pack is not published yet. That last case compiles
/// against a subset of the API rather than not compiling at all, and the lock records the rule so
/// the compromise is visible.
/// </summary>
public static class ReferencePackRule
{
    /// <summary>The package whose <c>ref/&lt;tfm&gt;</c> folder is the compile's framework reference set.</summary>
    public const string PackageId = "Microsoft.NETCore.App.Ref";

    /// <summary>
    /// The version and the rule that picked it, or null when the feed publishes no pack for this
    /// runtime (which is a hard failure: without reference assemblies nothing can compile).
    /// </summary>
    public static (string Version, string Rule)? Select(
        Version runtime,
        IEnumerable<string> published
    )
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(published);

        NuGetVersion wanted = new(runtime.Major, runtime.Minor, Math.Max(runtime.Build, 0));
        List<NuGetVersion> candidates = [];
        foreach (string text in published)
        {
            if (!NuGetVersion.TryParse(text, out NuGetVersion? version) || version.IsPrerelease)
            {
                continue;
            }

            if (
                version.Major == wanted.Major
                && version.Minor == wanted.Minor
                && version.Patch >= wanted.Patch
            )
            {
                candidates.Add(version);
            }
        }

        candidates.Sort();
        NuGetVersion? exact = candidates.Find(version => version.Patch == wanted.Patch);
        if (exact is not null)
        {
            return (exact.ToNormalizedString(), ReferencePackRules.Exact);
        }

        if (candidates.Count > 0)
        {
            return (candidates[0].ToNormalizedString(), ReferencePackRules.NearestAtLeast);
        }

        // The runtime is ahead of the feed: a patch is staged (the SDK or the machine has it) but its
        // reference pack is not published. An older pack of the same minor is a subset of the API
        // surface, so compiling against it is safe and is the only way to compile at all; refusing
        // would make an environment detail decide whether extensions work. The lock records the
        // rule, so the compromise is visible rather than silent.
        NuGetVersion? below = null;
        foreach (NuGetVersion version in PublishedBelow(published, wanted))
        {
            if (below is null || version > below)
            {
                below = version;
            }
        }

        return below is null ? null : (below.ToNormalizedString(), ReferencePackRules.NearestBelow);
    }

    private static IEnumerable<NuGetVersion> PublishedBelow(
        IEnumerable<string> published,
        NuGetVersion wanted
    )
    {
        foreach (string text in published)
        {
            if (
                NuGetVersion.TryParse(text, out NuGetVersion? version)
                && !version.IsPrerelease
                && version.Major == wanted.Major
                && version.Minor == wanted.Minor
                && version.Patch < wanted.Patch
            )
            {
                yield return version;
            }
        }
    }
}

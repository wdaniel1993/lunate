using Lunate.Extensibility;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// How the reference pack's version is chosen, recorded in the lock so a later restore resolves to
/// the same pack. The exact runtime version when the feed publishes it, otherwise the lowest
/// published pack of the same major/minor at or above the runtime — a pack older than the runtime
/// cannot describe it, and a newer one is still a superset of the API the host runs on.
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

        if (candidates.Count == 0)
        {
            return null;
        }

        candidates.Sort();
        NuGetVersion? exact = candidates.Find(version => version.Patch == wanted.Patch);
        return exact is not null
            ? (exact.ToNormalizedString(), ReferencePackRules.Exact)
            : (candidates[0].ToNormalizedString(), ReferencePackRules.NearestAtLeast);
    }
}

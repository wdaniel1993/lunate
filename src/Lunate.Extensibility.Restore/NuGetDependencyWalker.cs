using Lunate.Extensibility;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// Resolves the declared packages and their dependencies to exact versions with one documented
/// rule: a package takes the lowest version that satisfies every range asked of it. Requirements are
/// rebuilt from the current choices on each pass — declared ranges plus the dependency edges of
/// every chosen version — so an edge that disappears with a version change does not linger, and the
/// loop stops when no choice moves. The answer is deterministic, so the same graph always
/// serializes to the same lock.
/// </summary>
internal sealed class NuGetDependencyWalker(NuGetPackageFeed feed, NuGetFramework target)
{
    private const int MaxPasses = 64;

    private readonly NuGetPackageFeed _feed = feed;
    private readonly NuGetFramework _target = target;
    private readonly FrameworkReducer _reducer = new();

    /// <summary>One requirement on a package: the range, the text that asked for it, and who did.</summary>
    private sealed record Requirement(VersionRange Range, string Requested, bool Direct);

    private sealed record Choice(
        NuGetVersion Version,
        IReadOnlyList<PackageDependency> Dependencies
    );

    /// <summary>One resolved package: everything a lock entry needs except the downloaded bits.</summary>
    internal sealed record Resolved(
        string Id,
        NuGetVersion Version,
        string Requested,
        bool Direct,
        IReadOnlyList<PackageDependency> Dependencies
    );

    /// <summary>
    /// Resolves <paramref name="declared"/> into exact versions. Throws
    /// <see cref="InvalidOperationException"/> when a package is missing or no version satisfies the
    /// accumulated ranges: the message names the package, the ranges and who asked, because it is
    /// what the user sees as the restore failure.
    /// </summary>
    internal async Task<IReadOnlyList<Resolved>> ResolveAsync(
        IReadOnlyList<ExtensionPackageReference> declared,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, Choice> chosen = new(StringComparer.OrdinalIgnoreCase);
        for (int pass = 0; ; pass++)
        {
            if (pass > MaxPasses)
            {
                throw new InvalidOperationException(
                    "the dependency graph did not settle; its version ranges probably disagree in "
                        + "a way no single version satisfies."
                );
            }

            Dictionary<string, List<Requirement>> requirements = Build(declared, chosen);
            var changed = false;

            foreach (
                string dropped in chosen.Keys.Where(id => !requirements.ContainsKey(id)).ToList()
            )
            {
                chosen.Remove(dropped);
                changed = true;
            }

            foreach (
                string id in requirements.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList()
            )
            {
                NuGetVersion version = await SelectAsync(id, requirements[id], cancellationToken)
                    .ConfigureAwait(false);
                if (chosen.TryGetValue(id, out Choice? current) && current.Version == version)
                {
                    continue;
                }

                IReadOnlyList<PackageDependencyGroup> groups = await _feed
                    .DependencyGroupsAsync(id, version, cancellationToken)
                    .ConfigureAwait(false);
                chosen[id] = new Choice(version, Dependencies(groups));
                changed = true;
            }

            if (!changed)
            {
                return Materialize(chosen, requirements);
            }
        }
    }

    /// <summary>
    /// Every requirement in force: the header's declarations, then the dependency edges of each
    /// currently chosen version. Rebuilding it each pass is what keeps the graph honest.
    /// </summary>
    private static Dictionary<string, List<Requirement>> Build(
        IReadOnlyList<ExtensionPackageReference> declared,
        Dictionary<string, Choice> chosen
    )
    {
        Dictionary<string, List<Requirement>> requirements = new(StringComparer.OrdinalIgnoreCase);
        foreach (ExtensionPackageReference package in declared)
        {
            Add(
                requirements,
                package.Id,
                new Requirement(Parse(package), package.Written, Direct: true)
            );
        }

        foreach (Choice choice in chosen.Values)
        {
            foreach (PackageDependency dependency in choice.Dependencies)
            {
                VersionRange range = dependency.VersionRange ?? VersionRange.All;
                Add(
                    requirements,
                    dependency.Id,
                    new Requirement(range, range.ToNormalizedString(), Direct: false)
                );
            }
        }

        return requirements;
    }

    /// <summary>The lowest available version satisfying every requirement on the package.</summary>
    private async Task<NuGetVersion> SelectAsync(
        string id,
        IReadOnlyList<Requirement> requirements,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<NuGetVersion> available = await _feed
            .VersionsAsync(id, cancellationToken)
            .ConfigureAwait(false);
        if (available.Count == 0)
        {
            throw new InvalidOperationException(
                $"package '{id}' was not found on any configured feed ({string.Join(", ", _feed.Names)})."
            );
        }

        NuGetVersion? best = null;
        foreach (NuGetVersion version in available)
        {
            if (version.IsPrerelease && !AllowsPrerelease(requirements))
            {
                continue;
            }

            if (requirements.Any(requirement => !requirement.Range.Satisfies(version)))
            {
                continue;
            }

            if (best is null || version < best)
            {
                best = version;
            }
        }

        if (best is null)
        {
            throw new InvalidOperationException(
                $"no version of '{id}' satisfies "
                    + string.Join(
                        " and ",
                        requirements.Select(requirement =>
                            FormattableString.Invariant(
                                $"'{requirement.Range.ToNormalizedString()}' (asked by {requirement.Requested})"
                            )
                        )
                    )
                    + "."
            );
        }

        return best;
    }

    private static List<Resolved> Materialize(
        Dictionary<string, Choice> chosen,
        Dictionary<string, List<Requirement>> requirements
    )
    {
        List<Resolved> resolved = [];
        foreach (string id in chosen.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            Choice choice = chosen[id];
            Requirement first = requirements[id][0];
            resolved.Add(
                new Resolved(id, choice.Version, first.Requested, first.Direct, choice.Dependencies)
            );
        }

        return resolved;
    }

    private IReadOnlyList<PackageDependency> Dependencies(
        IReadOnlyList<PackageDependencyGroup> groups
    )
    {
        NuGetFramework? nearest = _reducer.GetNearest(
            _target,
            groups.Select(group => group.TargetFramework)
        );
        PackageDependencyGroup? group = nearest is null
            ? null
            : groups.FirstOrDefault(candidate => candidate.TargetFramework.Equals(nearest));

        // A package with dependency groups but none for this framework contributes nothing on this
        // TFM — NuGet's rule too — and the lock records the resulting empty edge set.
        return group is null ? [] : [.. group.Packages];
    }

    private static void Add(
        Dictionary<string, List<Requirement>> requirements,
        string id,
        Requirement requirement
    )
    {
        if (!requirements.TryGetValue(id, out List<Requirement>? existing))
        {
            requirements[id] = [requirement];
            return;
        }

        existing.Add(requirement);
    }

    private static bool AllowsPrerelease(IReadOnlyList<Requirement> requirements) =>
        requirements.Any(requirement =>
            requirement.Range.MinVersion is { IsPrerelease: true }
            || requirement.Range.MaxVersion is { IsPrerelease: true }
        );

    /// <summary>
    /// The declared version or range as a NuGet range. The scanner already accepted the syntax, so a
    /// failure here is a scanner bug; it still names the offending text rather than surfacing a bare
    /// format error.
    /// </summary>
    private static VersionRange Parse(ExtensionPackageReference package) =>
        VersionRange.TryParse(package.VersionRange, out VersionRange? range)
            ? range
            : throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"'{package.Written}' is not a NuGet version or version range."
                )
            );
}

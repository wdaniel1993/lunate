using Lunate.Extensibility;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// Resolves the declared packages and their dependencies to exact versions the way NuGet's
/// PackageReference resolver does, and refuses the graphs NuGet refuses. A direct
/// <c>#:package</c> declaration wins: it resolves inside its own range — the highest published
/// match for a floating range, the lowest otherwise — and a dependency edge that excludes that
/// version is a refusal rather than a silent widening. A transitively-only package takes the lowest
/// version that satisfies every range asked of it, which is the highest of the range minimums when
/// the ranges overlap. The refusals carry NuGet's codes — NU1605 for a downgrade, NU1608 for a
/// version outside a dependency range, NU1107 for a conflict no version satisfies — and end with
/// the explicit declaration that settles them. Requirements are rebuilt from the current choices
/// on each pass, so an edge that disappears with a version change does not linger, and the loop
/// stops when no choice moves; the same graph always serializes to the same lock.
/// </summary>
internal sealed class NuGetDependencyWalker(NuGetPackageFeed feed, NuGetFramework target)
{
    private const int MaxPasses = 64;

    // NuGet's diagnostic codes, so a refusal reads like the restore warning or error it replaces.
    private const string DowngradeCode = "NU1605";
    private const string OutsideRangeCode = "NU1608";
    private const string ConflictCode = "NU1107";

    private readonly NuGetPackageFeed _feed = feed;
    private readonly NuGetFramework _target = target;
    private readonly FrameworkReducer _reducer = new();

    /// <summary>
    /// One requirement on a package: the range, the text that asked for it, who asked, and whether
    /// a <c>#:package</c> declaration rather than a dependency edge asked.
    /// </summary>
    private sealed record Requirement(
        VersionRange Range,
        string Requested,
        string Asker,
        bool Direct
    );

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
    /// <see cref="InvalidOperationException"/> when a package is missing, no version satisfies a
    /// declared range, or the graph is one NuGet would warn about or fail: the message names the
    /// package, the ranges and who asked for each, because it is what the user sees as the restore
    /// failure.
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
    /// currently chosen version, attributed to the package that declares them. Rebuilding it each
    /// pass is what keeps the graph honest.
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
                new Requirement(Parse(package), package.Written, package.Written, Direct: true)
            );
        }

        foreach ((string parent, Choice choice) in chosen)
        {
            string asker = FormattableString.Invariant(
                $"{parent} {choice.Version.ToNormalizedString()}"
            );
            foreach (PackageDependency dependency in choice.Dependencies)
            {
                VersionRange range = dependency.VersionRange ?? VersionRange.All;
                Add(
                    requirements,
                    dependency.Id,
                    new Requirement(range, range.ToNormalizedString(), asker, Direct: false)
                );
            }
        }

        return requirements;
    }

    /// <summary>
    /// The version a package resolves to under NuGet's rules. A directly declared package is not
    /// moved by a dependency edge: it keeps the best version inside its own declaration and the
    /// edge that disagrees is a refusal. A transitive package takes the lowest version that
    /// satisfies every requirement, and a set no version satisfies is a conflict.
    /// </summary>
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

        List<Requirement> declared = [.. requirements.Where(requirement => requirement.Direct)];
        if (declared.Count > 0)
        {
            // NuGet treats a declared range as the project's own choice and never widens it to
            // satisfy an edge; a float takes the highest match, everything else the lowest.
            NuGetVersion? chosen = Best(available, declared, highest: IsFloating(declared));
            if (chosen is null)
            {
                throw Unsatisfiable(id, declared);
            }

            RefuseIfOutsideADependencyRange(id, chosen, requirements, declared[0], available);
            return chosen;
        }

        NuGetVersion? transitive = Best(available, requirements, highest: false);
        if (transitive is null)
        {
            throw Conflict(id, requirements, available);
        }

        return transitive;
    }

    /// <summary>
    /// The lowest (or, for a float, highest) published version satisfying every requirement. A
    /// float is matched by its own rule because NuGet's parsed range records no upper bound for
    /// one: <c>1.*</c> must not admit <c>2.0.0</c>.
    /// </summary>
    private static NuGetVersion? Best(
        IReadOnlyList<NuGetVersion> available,
        IReadOnlyList<Requirement> requirements,
        bool highest
    )
    {
        NuGetVersion? best = null;
        bool allowsPrerelease = AllowsPrerelease(requirements);
        foreach (NuGetVersion version in available)
        {
            if (version.IsPrerelease && !allowsPrerelease)
            {
                continue;
            }

            if (requirements.Any(requirement => !Satisfies(requirement.Range, version)))
            {
                continue;
            }

            if (best is null || (highest ? version > best : version < best))
            {
                best = version;
            }
        }

        return best;
    }

    /// <summary>
    /// Refuses a directly declared version that a dependency edge excludes. NuGet downgrades with
    /// NU1605 when the declaration sits below the edge's minimum and reports NU1608 when it sits
    /// outside a bound it never reaches; both are refused here, and the message names the
    /// declaration, the edge and the version that would settle them.
    /// </summary>
    private static void RefuseIfOutsideADependencyRange(
        string id,
        NuGetVersion chosen,
        IReadOnlyList<Requirement> requirements,
        Requirement declaration,
        IReadOnlyList<NuGetVersion> available
    )
    {
        foreach (Requirement requirement in requirements)
        {
            if (requirement.Direct || Satisfies(requirement.Range, chosen))
            {
                continue;
            }

            bool downgrade = requirement.Range.MinVersion is { } minimum && chosen < minimum;
            string code = downgrade ? DowngradeCode : OutsideRangeCode;
            string what = downgrade
                ? "package downgrade"
                : "package version outside a dependency range";
            List<Requirement> transitive = [.. requirements.Where(entry => !entry.Direct)];
            string fix = Fix(id, transitive, available, chosen);
            string version = chosen.ToNormalizedString();
            string range = requirement.Range.ToNormalizedString();
            throw new InvalidOperationException(
                $"{code}: {what}: '{id}' resolves to {version} (declared by "
                    + $"'{declaration.Requested}'), but '{requirement.Asker}' requires '{range}'; "
                    + fix
            );
        }
    }

    /// <summary>NU1107: no published version satisfies every range the graph asks for.</summary>
    private static InvalidOperationException Conflict(
        string id,
        IReadOnlyList<Requirement> requirements,
        IReadOnlyList<NuGetVersion> available
    ) =>
        new(
            $"{ConflictCode}: version conflict for '{id}': no version satisfies "
                + string.Join(
                    " and ",
                    requirements.Select(requirement =>
                        $"'{requirement.Range.ToNormalizedString()}' (asked by "
                        + $"'{requirement.Asker}')"
                    )
                )
                + $"; {Fix(id, requirements, available, available[^1])}"
        );

    /// <summary>
    /// The declaration that settles a refusal: the lowest published version that satisfies the
    /// constraints that disagreed, else the highest minimum the graph asks for — the version NuGet
    /// itself suggests when it reports a conflict.
    /// </summary>
    private static string Fix(
        string id,
        IReadOnlyList<Requirement> constraints,
        IReadOnlyList<NuGetVersion> available,
        NuGetVersion fallback
    )
    {
        NuGetVersion suggestion =
            Best(available, constraints, highest: false) ?? HighestMinimum(constraints) ?? fallback;

        // A hint has to name something a restore can fetch: the highest minimum is not published
        // when the feed skips a patch.
        if (!available.Contains(suggestion))
        {
            suggestion =
                available.FirstOrDefault(version => version >= suggestion) ?? available[^1];
        }

        return FormattableString.Invariant(
            $"add #:package {id}@{suggestion.ToNormalizedString()} satisfying all ranges"
        );
    }

    private static NuGetVersion? HighestMinimum(IReadOnlyList<Requirement> requirements)
    {
        NuGetVersion? highest = null;
        foreach (Requirement requirement in requirements)
        {
            if (requirement.Range.MinVersion is not { } minimum)
            {
                continue;
            }

            if (highest is null || minimum > highest)
            {
                highest = minimum;
            }
        }

        return highest;
    }

    /// <summary>No published version satisfies the declared range: the declaration itself fails.</summary>
    private static InvalidOperationException Unsatisfiable(
        string id,
        IReadOnlyList<Requirement> declared
    ) =>
        new(
            $"no version of '{id}' satisfies "
                + string.Join(
                    " and ",
                    declared.Select(requirement =>
                        $"'{requirement.Range.ToNormalizedString()}' (asked by "
                        + $"{requirement.Requested})"
                    )
                )
                + "."
        );

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

    /// <summary>
    /// Whether a version satisfies a range. A float is matched by its float rule: NuGet's parsed
    /// range carries no upper bound for <c>1.*</c>, so the range alone would admit <c>2.0.0</c>.
    /// </summary>
    private static bool Satisfies(VersionRange range, NuGetVersion version) =>
        range.Float is { } floating ? floating.Satisfies(version) : range.Satisfies(version);

    private static bool IsFloating(IReadOnlyList<Requirement> requirements) =>
        requirements.Any(requirement => requirement.Range.Float is not null);

    private static bool AllowsPrerelease(IReadOnlyList<Requirement> requirements) =>
        requirements.Any(requirement =>
            requirement.Range.MinVersion is { IsPrerelease: true }
            || requirement.Range.MaxVersion is { IsPrerelease: true }
            || requirement.Range.Float is { IncludePrerelease: true }
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

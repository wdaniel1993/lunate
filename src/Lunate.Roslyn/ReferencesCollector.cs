namespace Lunate.Roslyn;

/// <summary>The bounded, deduplicated usage list produced by <see cref="ReferencesCollector"/>.</summary>
internal readonly record struct BoundedReferences(
    IReadOnlyList<ReferenceLocation> Items,
    int Total,
    bool Truncated
);

/// <summary>
/// Deduplicates, orders and bounds raw usage locations: identical (file, line, column) triples
/// collapse, the order is file (Ordinal) then line then column, and the cap keeps the first 200
/// while counting every distinct usage.
/// </summary>
internal static class ReferencesCollector
{
    internal const int MaxReferences = 200;

    public static BoundedReferences Build(IEnumerable<ReferenceLocation> locations)
    {
        var capped = new CappedList<ReferenceLocation>(MaxReferences);
        foreach (
            var location in locations
                .Distinct()
                .OrderBy(location => location.File, StringComparer.Ordinal)
                .ThenBy(location => location.Line)
                .ThenBy(location => location.Column)
        )
        {
            capped.Add(location);
        }

        return new BoundedReferences(capped.Items, capped.Total, capped.Truncated);
    }
}

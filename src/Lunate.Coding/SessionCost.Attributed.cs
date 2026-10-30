using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding;

internal static partial class UsageCost
{
    /// <summary>
    /// Prices each schema-4 message usage once, then derives the usage and provider views from the
    /// same records. Unknown attribution stays under an explicit unknown group; unknown prices stay
    /// unpriced rather than becoming zero.
    /// </summary>
    public static UsageCostReport? SummarizeAttributedSession(
        ModelCatalog catalog,
        Session session,
        string? fallbackModelId = null
    )
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(session);

        List<AttributedRecord> records = [];
        foreach (SessionMessageEntry entry in session.Entries.OfType<SessionMessageEntry>())
        {
            if (entry.Usage is not { } usage)
            {
                continue;
            }

            ModelUsageAttribution? attribution = usage.Attribution;
            string? modelId = entry.Model ?? fallbackModelId;
            UsageLanes lanes = UsageLanes.From(usage);
            records.Add(
                new AttributedRecord(
                    attribution?.Usage ?? "unknown",
                    attribution?.ProviderName ?? "unknown",
                    attribution?.ProviderKind,
                    modelId,
                    lanes,
                    ForRecord(catalog, modelId, lanes)
                )
            );
        }

        if (records.Count == 0)
        {
            return null;
        }

        IReadOnlyList<UsageCostLine> byUsage = records
            .GroupBy(record => record.Usage, StringComparer.Ordinal)
            .Select(group => SummarizeGroup(group.Key, providerKind: null, group))
            .ToArray();
        IReadOnlyList<UsageCostLine> byProvider = records
            .GroupBy(record => (record.ProviderName, record.ProviderKind))
            .Select(group => SummarizeGroup(group.Key.ProviderName, group.Key.ProviderKind, group))
            .ToArray();
        decimal[] priced =
        [
            .. records
                .Where(record => record.Cost is not null)
                .Select(record => record.Cost!.Value),
        ];

        return new UsageCostReport(
            byUsage,
            byProvider,
            priced.Length == 0 ? null : priced.Sum(),
            records.Any(record => record.Cost is null)
        );
    }

    private static UsageCostLine SummarizeGroup(
        string name,
        string? providerKind,
        IEnumerable<AttributedRecord> records
    )
    {
        AttributedRecord[] items = [.. records];
        decimal? cost = items.All(item => item.Cost is not null)
            ? items.Sum(item => item.Cost!.Value)
            : null;
        return new UsageCostLine(
            name,
            providerKind,
            items
                .Select(item => item.ModelId ?? "unknown")
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            items
                .Select(item => item.Lanes)
                .Aggregate(default(UsageLanes), (sum, lanes) => sum.Add(lanes)),
            cost
        );
    }

    private sealed record AttributedRecord(
        string Usage,
        string ProviderName,
        string? ProviderKind,
        string? ModelId,
        UsageLanes Lanes,
        decimal? Cost
    );
}

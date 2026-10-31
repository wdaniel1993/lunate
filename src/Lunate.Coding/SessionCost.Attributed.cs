using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding;

internal static partial class UsageCost
{
    /// <summary>
    /// Prices each schema-4 model usage once, then derives the usage and provider views from the
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

            records.Add(
                CreateAttributedRecord(
                    catalog,
                    usage,
                    usage.Attribution,
                    entry.Model ?? fallbackModelId
                )
            );
        }

        foreach (SessionModelUsageEntry entry in session.Entries.OfType<SessionModelUsageEntry>())
        {
            if (entry.Usage is not { } usage)
            {
                continue;
            }

            records.Add(
                CreateAttributedRecord(
                    catalog,
                    usage,
                    entry.Attribution,
                    entry.Model ?? fallbackModelId
                )
            );
        }

        if (records.Count == 0)
        {
            return null;
        }

        IReadOnlyList<UsageCostLine> byUsage = records
            .GroupBy(record => (record.Usage, record.DecisionMethod))
            .Select(group =>
                SummarizeGroup(group.Key.Usage, providerKind: null, group, group.Key.DecisionMethod)
            )
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
        IEnumerable<AttributedRecord> records,
        string? decisionMethod = null
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
            cost,
            decisionMethod
        );
    }

    private static AttributedRecord CreateAttributedRecord(
        ModelCatalog catalog,
        SessionUsage usage,
        ModelUsageAttribution? attribution,
        string? modelId
    )
    {
        UsageLanes lanes = UsageLanes.From(usage);
        return new AttributedRecord(
            attribution?.Usage ?? "unknown",
            attribution?.ProviderName ?? "unknown",
            attribution?.ProviderKind,
            attribution?.Usage == "decision" ? attribution.DecisionMethod : null,
            modelId,
            lanes,
            ForRecord(catalog, modelId, lanes)
        );
    }

    private sealed record AttributedRecord(
        string Usage,
        string ProviderName,
        string? ProviderKind,
        string? DecisionMethod,
        string? ModelId,
        UsageLanes Lanes,
        decimal? Cost
    );
}

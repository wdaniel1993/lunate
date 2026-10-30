using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding;

/// <summary>
/// The four token lanes of one usage record: input, output and the provider's cache read and write
/// counts. A provider that reports no cache count contributes zero, because a display sum cannot
/// distinguish "unknown" from "none" once it has been added.
/// </summary>
internal readonly record struct UsageLanes(long Input, long Output, long CacheRead, long CacheWrite)
{
    private const string CacheCreationInputTokensKey = "CacheCreationInputTokens";

    /// <summary>Maps a provider usage record; unreported counts become zero.</summary>
    public static UsageLanes From(UsageDetails usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        long cacheWrite = 0;
        if (
            usage.AdditionalCounts is { } counts
            && counts.TryGetValue(CacheCreationInputTokensKey, out long reportedWrite)
        )
        {
            cacheWrite = reportedWrite;
        }

        return new UsageLanes(
            usage.InputTokenCount ?? 0,
            usage.OutputTokenCount ?? 0,
            usage.CachedInputTokenCount ?? 0,
            cacheWrite
        );
    }

    /// <summary>Maps a recorded session usage; a schema 1/2 entry has no cache counts.</summary>
    public static UsageLanes From(SessionUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return new UsageLanes(
            usage.Input,
            usage.Output,
            usage.CacheRead ?? 0,
            usage.CacheWrite ?? 0
        );
    }

    /// <summary>Sums two lane records, lane by lane.</summary>
    public UsageLanes Add(UsageLanes other) =>
        new(
            Input + other.Input,
            Output + other.Output,
            CacheRead + other.CacheRead,
            CacheWrite + other.CacheWrite
        );
}

/// <summary>One model's aggregated lanes and their display cost in US dollars.</summary>
internal sealed record ModelCostLine(string ModelId, UsageLanes Lanes, decimal Cost);

/// <summary>A session's or a turn's cost per model plus the total; only fully priced input.</summary>
internal sealed record CostReport(IReadOnlyList<ModelCostLine> Models, decimal Total);

/// <summary>One usage or provider subtotal, with the models that contributed to it.</summary>
internal sealed record UsageCostLine(
    string Name,
    string? ProviderKind,
    IReadOnlyList<string> ModelIds,
    UsageLanes Lanes,
    decimal? Cost
);

/// <summary>Cost totals by usage and provider; an unpriced group keeps a null cost.</summary>
internal sealed record UsageCostReport(
    IReadOnlyList<UsageCostLine> ByUsage,
    IReadOnlyList<UsageCostLine> ByProvider,
    decimal? Total,
    bool HasUnpriced
);

/// <summary>
/// Display-only cost aggregation over the catalog's prices: per record, per turn, per model and per
/// lane. A record whose model is unknown, unpriced or missing a rate for a used lane is not fully
/// priced, so the whole report is absent rather than a partial or zero amount (design D6).
/// </summary>
internal static partial class UsageCost
{
    /// <summary>
    /// One record's cost, or null when the model id is absent, the model is unknown or unpriced, a
    /// lane count is negative, a used lane has no rate, or the id is ambiguous. Ambiguity and
    /// corruption are swallowed because the cost is display-only and must never break the report.
    /// </summary>
    public static decimal? ForRecord(ModelCatalog catalog, string? modelId, UsageLanes lanes)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (HasNegativeLane(lanes))
        {
            return null;
        }

        if (string.IsNullOrEmpty(modelId) || Find(catalog, modelId) is not { } model)
        {
            return null;
        }

        return CostCalculator.Compute(
            model,
            lanes.Input,
            lanes.Output,
            lanes.CacheRead,
            lanes.CacheWrite
        );
    }

    /// <summary>
    /// Aggregates records by model id in first-seen order and prices each model's summed lanes.
    /// Null when there is nothing to report, a record has no model id or a negative lane, or any
    /// model cannot be fully priced. Each record is validated before aggregating so a corrupted
    /// lane cannot hide behind a positive one. The total is the sum of the model costs.
    /// </summary>
    public static CostReport? Summarize(
        ModelCatalog catalog,
        IEnumerable<(string? ModelId, UsageLanes Lanes)> records
    )
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(records);

        List<string> order = [];
        Dictionary<string, UsageLanes> lanesByModel = new(StringComparer.Ordinal);
        foreach ((string? modelId, UsageLanes lanes) in records)
        {
            // Before aggregating: a negative lane from a corrupted session file is unpriced, and a
            // positive record for the same model must not hide it in the sum.
            if (HasNegativeLane(lanes))
            {
                return null;
            }

            if (string.IsNullOrEmpty(modelId))
            {
                return null;
            }

            if (lanesByModel.TryGetValue(modelId, out UsageLanes existing))
            {
                lanesByModel[modelId] = existing.Add(lanes);
            }
            else
            {
                lanesByModel[modelId] = lanes;
                order.Add(modelId);
            }
        }

        List<ModelCostLine> lines = new(order.Count);
        decimal total = 0m;
        foreach (string modelId in order)
        {
            UsageLanes lanes = lanesByModel[modelId];
            if (ForRecord(catalog, modelId, lanes) is not decimal cost)
            {
                return null;
            }

            lines.Add(new ModelCostLine(modelId, lanes, cost));
            total += cost;
        }

        return lines.Count == 0 ? null : new CostReport(lines, total);
    }

    /// <summary>A turn's report: every event's lanes, attributed to its model or the fallback.</summary>
    public static CostReport? Summarize(
        ModelCatalog catalog,
        IEnumerable<UsageUpdated> events,
        string? fallbackModelId
    )
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(events);
        return Summarize(
            catalog,
            events.Select(usage => (usage.ModelId ?? fallbackModelId, UsageLanes.From(usage.Usage)))
        );
    }

    /// <summary>
    /// A session file's report from its recorded message usages, keyed by the entry's model (or
    /// the fallback id). A schema 1/2 session is costed only when every referenced model has no
    /// cache rate: an old input total hides an unknown cache split, so the cost would be wrong.
    /// </summary>
    public static CostReport? SummarizeSession(
        ModelCatalog catalog,
        Session session,
        string? fallbackModelId
    )
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(session);

        List<(string? ModelId, UsageLanes Lanes)> records = [];
        foreach (SessionMessageEntry entry in session.Entries.OfType<SessionMessageEntry>())
        {
            if (entry.Usage is { } usage)
            {
                records.Add((entry.Model ?? fallbackModelId, UsageLanes.From(usage)));
            }
        }

        if (records.Count == 0)
        {
            return null;
        }

        if (session.Schema < 3)
        {
            foreach ((string? modelId, _) in records)
            {
                if (modelId is null)
                {
                    continue;
                }

                ModelInfo? model = Find(catalog, modelId);
                if (model?.Pricing is { } pricing)
                {
                    if (pricing.CacheRead is not null || pricing.CacheWrite is not null)
                    {
                        return null;
                    }
                }
            }
        }

        return Summarize(catalog, records);
    }

    /// <summary>A negative lane is corrupted input, never a price: the record is unpriced.</summary>
    private static bool HasNegativeLane(UsageLanes lanes) =>
        lanes.Input < 0 || lanes.Output < 0 || lanes.CacheRead < 0 || lanes.CacheWrite < 0;

    private static ModelInfo? Find(ModelCatalog catalog, string modelId)
    {
        try
        {
            return catalog.Find(modelId);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

using System.Globalization;

namespace Lunate.Tui;

/// <summary>One usage or provider cost subtotal prepared by the host.</summary>
public sealed record CostGroupView(
    string Name,
    string? ProviderKind,
    IReadOnlyList<string> ModelIds,
    long Input,
    long Output,
    long CacheRead,
    long CacheWrite,
    string? Cost
)
{
    /// <summary>The decision method shown beside decision-usage totals, when attributed.</summary>
    public string? DecisionMethod { get; init; }
}

/// <summary>The priced and unpriced cost groups shown by the read-only cost command.</summary>
public sealed record CostReportView(
    IReadOnlyList<CostGroupView> ByUsage,
    IReadOnlyList<CostGroupView> ByProvider,
    string? Total,
    bool HasUnpriced
);

/// <summary>Renders host-computed cost groups without depending on model or provider projects.</summary>
public static class UsageCostRenderer
{
    /// <summary>Returns escaped-ready lines grouped by usage and provider.</summary>
    public static IReadOnlyList<string> Render(CostReportView report)
    {
        ArgumentNullException.ThrowIfNull(report);
        List<string> lines = ["cost by usage:"];
        AddGroups(lines, report.ByUsage, providerGroups: false);
        lines.Add("cost by provider:");
        AddGroups(lines, report.ByProvider, providerGroups: true);
        lines.Add("total: " + (report.Total ?? "unpriced"));
        if (report.HasUnpriced && report.Total is not null)
        {
            lines.Add("some usage is unpriced; total excludes it");
        }

        return lines;
    }

    private static void AddGroups(
        List<string> lines,
        IReadOnlyList<CostGroupView> groups,
        bool providerGroups
    )
    {
        foreach (CostGroupView group in groups)
        {
            string name =
                providerGroups && group.ProviderKind is { Length: > 0 } kind
                    ? $"{group.Name} ({kind})"
                    : group.Name;
            if (!providerGroups && group.DecisionMethod is { Length: > 0 } method)
            {
                name += $" ({method})";
            }

            lines.Add(
                FormattableString.Invariant(
                    $"  {name}: {group.Input.ToString("N0", CultureInfo.InvariantCulture)} in / {group.Output.ToString("N0", CultureInfo.InvariantCulture)} out / {group.CacheRead.ToString("N0", CultureInfo.InvariantCulture)} cache read / {group.CacheWrite.ToString("N0", CultureInfo.InvariantCulture)} cache write = {group.Cost ?? "unpriced"}; models: {string.Join(", ", group.ModelIds)}"
                )
            );
        }
    }
}

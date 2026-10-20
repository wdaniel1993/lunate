using System.Globalization;

namespace Lunate.Ai;

/// <summary>
/// Display-only cost helpers over the catalogue's per-million-token prices. Prices are never used
/// for budget decisions, approvals or routing.
/// </summary>
public static class CostCalculator
{
    /// <summary>
    /// The cost of one usage record in US dollars, or null when the model is missing, unpriced or a
    /// used lane has no rate. Delegates to <see cref="ModelPricing.ComputeCost"/> so an unknown price
    /// is null, never zero.
    /// </summary>
    public static decimal? Compute(
        ModelInfo? model,
        long inputTokens,
        long outputTokens,
        long cacheReadTokens = 0,
        long cacheWriteTokens = 0
    ) => model?.Pricing?.ComputeCost(inputTokens, outputTokens, cacheReadTokens, cacheWriteTokens);

    /// <summary>
    /// Formats a cost for display with the invariant culture: <c>&lt;$0.0001</c> for a positive cost
    /// that rounds to zero at four decimals, four decimals below one dollar and two decimals from
    /// one dollar. Display-only: never parse the result back for arithmetic.
    /// </summary>
    public static string Format(decimal cost)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cost);
        if (cost > 0m && decimal.Round(cost, 4, MidpointRounding.AwayFromZero) == 0m)
        {
            return "<$0.0001";
        }

        string number =
            cost < 1m
                ? cost.ToString("F4", CultureInfo.InvariantCulture)
                : cost.ToString("F2", CultureInfo.InvariantCulture);
        return "$" + number;
    }
}

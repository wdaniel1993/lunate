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
    /// that rounds to zero at four decimals, four decimals for a positive cost below one dollar and
    /// two decimals for zero or one dollar and above. The cost is rounded once, to four decimals,
    /// before the scale is chosen, so a value below one dollar never prints as <c>$1.0000</c>.
    /// Display-only: never parse the result back for arithmetic.
    /// </summary>
    public static string Format(decimal cost)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cost);
        decimal rounded = decimal.Round(cost, 4, MidpointRounding.AwayFromZero);
        if (cost > 0m && rounded == 0m)
        {
            return "<$0.0001";
        }

        string number =
            rounded == 0m || rounded >= 1m
                ? rounded.ToString("F2", CultureInfo.InvariantCulture)
                : rounded.ToString("F4", CultureInfo.InvariantCulture);
        return "$" + number;
    }
}

namespace Lunate.Ai;

/// <summary>
/// Optional catalogue prices in US dollars per million tokens. A model without a pricing object
/// is unpriced; a lane with a positive token count needs its rate or the whole record is unpriced.
/// </summary>
public sealed record ModelPricing(
    decimal? Input = null,
    decimal? Output = null,
    decimal? CacheRead = null,
    decimal? CacheWrite = null
)
{
    /// <summary>
    /// Computes the cost of one usage record in US dollars. Cached input is removed from the
    /// uncached input lane; a missing rate for a used lane returns null (unpriced) rather than zero.
    /// </summary>
    public decimal? ComputeCost(
        long inputTokens,
        long outputTokens,
        long cacheReadTokens,
        long cacheWriteTokens
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheReadTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheWriteTokens);

        long uncachedTokens = Math.Max(0, inputTokens - cacheReadTokens - cacheWriteTokens);
        decimal cost = 0m;
        if (uncachedTokens > 0)
        {
            if (Input is not decimal inputRate)
            {
                return null;
            }

            cost += uncachedTokens * inputRate;
        }

        if (cacheReadTokens > 0)
        {
            if (CacheRead is not decimal cacheReadRate)
            {
                return null;
            }

            cost += cacheReadTokens * cacheReadRate;
        }

        if (cacheWriteTokens > 0)
        {
            if (CacheWrite is not decimal cacheWriteRate)
            {
                return null;
            }

            cost += cacheWriteTokens * cacheWriteRate;
        }

        if (outputTokens > 0)
        {
            if (Output is not decimal outputRate)
            {
                return null;
            }

            cost += outputTokens * outputRate;
        }

        return cost / 1_000_000m;
    }
}

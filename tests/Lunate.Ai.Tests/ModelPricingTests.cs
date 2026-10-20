using System.Globalization;

namespace Lunate.Ai.Tests;

public sealed class ModelPricingTests
{
    [Fact]
    public void ComputeCost_prices_each_cache_lane_at_its_own_rate()
    {
        var pricing = new ModelPricing(2.5m, 10m, 1.25m, 3.75m);

        decimal? cost = pricing.ComputeCost(1_000_000, 10_000, 200_000, 100_000);

        Assert.Equal(2.475m, cost);
    }

    [Fact]
    public void ComputeCost_is_unpriced_when_a_used_lane_has_no_rate()
    {
        var pricing = new ModelPricing(2.5m, 10m, CacheRead: null, CacheWrite: 3.75m);

        decimal? cost = pricing.ComputeCost(1_000_000, 10_000, 200_000, 0);

        Assert.Null(cost);
    }

    [Fact]
    public void ComputeCost_does_not_need_a_rate_for_an_unused_lane()
    {
        var pricing = new ModelPricing(1m, 2m);

        decimal? cost = pricing.ComputeCost(1_000_000, 100, 0, 0);

        Assert.Equal(1.0002m, cost);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void ComputeCost_rejects_negative_token_counts(
        long input,
        long output,
        long cacheRead,
        long cacheWrite
    )
    {
        var pricing = new ModelPricing(1m, 2m, 3m, 4m);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            pricing.ComputeCost(input, output, cacheRead, cacheWrite);
        });
    }

    [Fact]
    public void ComputeCost_is_culture_invariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-AT");
            var pricing = new ModelPricing(2.5m, 10m, 1.25m, 3.75m);

            decimal? cost = pricing.ComputeCost(1_000_000, 10_000, 200_000, 100_000);

            Assert.Equal(2.475m, cost);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}

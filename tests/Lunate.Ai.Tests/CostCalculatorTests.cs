using System.Globalization;

namespace Lunate.Ai.Tests;

public sealed class CostCalculatorTests
{
    private static ModelInfo Priced(ModelPricing? pricing) =>
        new("priced", "openai", null, 128_000, SupportsTools: true) { Pricing = pricing };

    [Fact]
    public void Compute_is_null_for_a_missing_model()
    {
        decimal? cost = CostCalculator.Compute(null, 1_000_000, 10_000);

        Assert.Null(cost);
    }

    [Fact]
    public void Compute_is_null_when_the_model_has_no_pricing()
    {
        decimal? cost = CostCalculator.Compute(Priced(null), 1_000_000, 10_000);

        Assert.Null(cost);
    }

    [Fact]
    public void Compute_prices_the_cache_lanes_of_a_model()
    {
        var model = Priced(new ModelPricing(2.5m, 10m, 1.25m, 3.75m));

        decimal? cost = CostCalculator.Compute(model, 1_000_000, 10_000, 200_000, 100_000);

        Assert.Equal(2.475m, cost);
    }

    [Fact]
    public void Format_uses_four_decimals_below_one_dollar()
    {
        Assert.Equal("$0.0005", CostCalculator.Format(0.00045m));
    }

    [Fact]
    public void Format_uses_two_decimals_from_one_dollar()
    {
        Assert.Equal("$1.24", CostCalculator.Format(1.239m));
    }

    [Fact]
    public void Format_marks_a_positive_cost_below_the_four_decimal_precision()
    {
        Assert.Equal("<$0.0001", CostCalculator.Format(0.00004m));
    }

    [Fact]
    public void Format_zero_is_two_decimals()
    {
        Assert.Equal("$0.00", CostCalculator.Format(0m));
    }

    [Fact]
    public void Format_rounds_up_across_the_dollar_boundary_on_one_scale()
    {
        Assert.Equal("$1.00", CostCalculator.Format(0.99996m));
    }

    [Fact]
    public void Format_rejects_a_negative_cost()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CostCalculator.Format(-0.01m));
    }

    [Fact]
    public void Format_is_culture_invariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-AT");

            Assert.Equal("$0.0005", CostCalculator.Format(0.00045m));
            Assert.Equal("$1.24", CostCalculator.Format(1.239m));
            Assert.Equal("<$0.0001", CostCalculator.Format(0.00004m));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}

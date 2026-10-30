using System.Globalization;

namespace Lunate.Tui.Tests;

public sealed class UsageCostRendererTests
{
    [Fact]
    public void Render_shows_usage_and_provider_groups_with_model_ids()
    {
        var report = new CostReportView(
            [new CostGroupView("chat", null, ["model-a"], 1000, 500, 0, 0, "$0.0005")],
            [new CostGroupView("primary", "openai", ["model-a"], 1000, 500, 0, 0, "$0.0005")],
            "$0.0005",
            HasUnpriced: false
        );

        Assert.Equal(
            [
                "cost by usage:",
                "  chat: 1,000 in / 500 out / 0 cache read / 0 cache write = $0.0005; models: model-a",
                "cost by provider:",
                "  primary (openai): 1,000 in / 500 out / 0 cache read / 0 cache write = $0.0005; models: model-a",
                "total: $0.0005",
            ],
            UsageCostRenderer.Render(report)
        );
    }

    [Fact]
    public void Render_marks_unknown_prices_and_formats_counts_invariantly()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-AT");
            var report = new CostReportView(
                [new CostGroupView("decision", null, ["model-b"], 1500, 0, 0, 0, null)],
                [new CostGroupView("router", "openrouter", ["model-b"], 1500, 0, 0, 0, null)],
                null,
                HasUnpriced: true
            );

            Assert.Equal(
                [
                    "cost by usage:",
                    "  decision: 1,500 in / 0 out / 0 cache read / 0 cache write = unpriced; models: model-b",
                    "cost by provider:",
                    "  router (openrouter): 1,500 in / 0 out / 0 cache read / 0 cache write = unpriced; models: model-b",
                    "total: unpriced",
                ],
                UsageCostRenderer.Render(report)
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}

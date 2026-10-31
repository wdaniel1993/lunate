using System.Globalization;
using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class UsageCostTests
{
    private const string PricedModel = """
        { "id": "fixture-priced", "provider": "fixture", "contextWindow": 1000, "supportsTools": true, "pricing": { "input": 2.5, "output": 10, "cacheRead": 1.25, "cacheWrite": 3.75 } }
        """;

    private const string OtherPricedModel = """
        { "id": "fixture-other", "provider": "fixture", "contextWindow": 1000, "supportsTools": true, "pricing": { "input": 1, "output": 2 } }
        """;

    private const string UnpricedModel = """
        { "id": "fixture-unpriced", "provider": "fixture", "contextWindow": 1000, "supportsTools": true }
        """;

    [Fact]
    public void Summarize_aggregates_each_model_by_lane_and_totals_its_cost()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(temp, PricedModel);

        CostReport report = Assert.IsType<CostReport>(
            UsageCost.Summarize(
                catalog,
                [
                    ("fixture-priced", new UsageLanes(600_000, 10_000, 100_000, 0)),
                    ("fixture-priced", new UsageLanes(400_000, 0, 100_000, 100_000)),
                ]
            )
        );

        ModelCostLine line = Assert.Single(report.Models);
        Assert.Equal("fixture-priced", line.ModelId);
        Assert.Equal(new UsageLanes(1_000_000, 10_000, 200_000, 100_000), line.Lanes);
        Assert.Equal(2.475m, line.Cost);
        Assert.Equal(2.475m, report.Total);
    }

    [Fact]
    public void Summarize_keeps_a_second_model_on_its_own_line()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(temp, PricedModel, OtherPricedModel);

        CostReport report = Assert.IsType<CostReport>(
            UsageCost.Summarize(
                catalog,
                [
                    ("fixture-other", new UsageLanes(1_000_000, 0, 0, 0)),
                    ("fixture-priced", new UsageLanes(1_000_000, 0, 0, 0)),
                ]
            )
        );

        Assert.Equal(["fixture-other", "fixture-priced"], report.Models.Select(m => m.ModelId));
        Assert.Equal(1m, report.Models[0].Cost);
        Assert.Equal(2.5m, report.Models[1].Cost);
        Assert.Equal(3.5m, report.Total);
    }

    [Fact]
    public void Summarize_returns_null_when_any_record_names_an_unpriced_model()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(temp, PricedModel, UnpricedModel);

        Assert.Null(
            UsageCost.Summarize(
                catalog,
                [
                    ("fixture-priced", new UsageLanes(1_000_000, 0, 0, 0)),
                    ("fixture-unpriced", new UsageLanes(1_000_000, 0, 0, 0)),
                ]
            )
        );
    }

    [Fact]
    public void Summarize_returns_null_when_a_used_cache_lane_has_no_rate()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(temp, OtherPricedModel);

        Assert.Null(
            UsageCost.Summarize(catalog, [("fixture-other", new UsageLanes(1_500, 50, 1_200, 0))])
        );
    }

    [Fact]
    public void ForRecord_returns_null_when_any_lane_is_negative()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(temp, PricedModel);

        Assert.Null(UsageCost.ForRecord(catalog, "fixture-priced", new UsageLanes(1000, 0, -1, 0)));
    }

    [Fact]
    public void Summarize_returns_null_when_a_negative_record_cancels_a_positive_one()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(temp, PricedModel);

        Assert.Null(
            UsageCost.Summarize(
                catalog,
                [
                    ("fixture-priced", new UsageLanes(1_000_000, 0, 1_000_000, 0)),
                    ("fixture-priced", new UsageLanes(0, 0, -1_000_000, 0)),
                ]
            )
        );
    }

    [Fact]
    public void Summarize_returns_null_for_an_empty_record_list()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(temp, PricedModel);

        Assert.Null(UsageCost.Summarize(catalog, Array.Empty<(string?, UsageLanes)>()));
    }

    [Fact]
    public void Summarize_attributed_session_groups_by_usage_and_provider_once()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(temp, PricedModel, OtherPricedModel);
        Session session = Session.Create(temp.File("session.jsonl"), "/work");
        AddUsage(session, "fixture-priced", "chat", "shared", "primary", "openai", 1_000_000);
        AddUsage(session, "fixture-priced", "compaction", "shared", "primary", "openai", 1_000_000);
        session.AppendModelUsage(
            "fixture-other",
            new SessionUsage(1_000_000, 0),
            new ModelUsageAttribution(
                "decision",
                "decision-profile",
                "router",
                "openrouter",
                "adapter",
                0.8d
            ),
            decisionOption: "deny"
        );

        UsageCostReport report = Assert.IsType<UsageCostReport>(
            UsageCost.SummarizeAttributedSession(catalog, session)
        );

        Assert.Equal(6m, report.Total);
        Assert.Equal(["chat", "compaction", "decision"], report.ByUsage.Select(row => row.Name));
        Assert.Equal("adapter", report.ByUsage[2].DecisionMethod);
        Assert.Equal(2.5m, report.ByUsage[0].Cost);
        Assert.Equal(["fixture-priced"], report.ByUsage[0].ModelIds);
        Assert.Equal(["primary", "router"], report.ByProvider.Select(row => row.Name));
        Assert.Equal(5m, report.ByProvider[0].Cost);
        Assert.Equal(["fixture-priced"], report.ByProvider[0].ModelIds);
    }

    [Fact]
    public void Summarize_attributed_decision_cost_keeps_method_visible()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(temp, OtherPricedModel);
        Session session = Session.Create(temp.File("session.jsonl"), "/work");
        session.AppendModelUsage(
            "fixture-other",
            new SessionUsage(1_000, 0),
            new ModelUsageAttribution("decision", "decision-profile", "router", "openrouter", "native", 0.8d)
        );
        session.AppendModelUsage(
            "fixture-other",
            new SessionUsage(2_000, 0),
            new ModelUsageAttribution("decision", "decision-profile", "router", "openrouter", "adapter", 0.7d)
        );

        UsageCostReport report = Assert.IsType<UsageCostReport>(
            UsageCost.SummarizeAttributedSession(catalog, session)
        );

        Assert.Equal(
            ["decision (native)", "decision (adapter)"],
            report.ByUsage.Select(line => line.Name)
        );
        Assert.Equal(0.001m, report.ByUsage[0].Cost);
        Assert.Equal(0.002m, report.ByUsage[1].Cost);
    }

    [Fact]
    public void Summarize_attributed_session_marks_unknown_positive_lanes_unpriced()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(temp, UnpricedModel);
        Session session = Session.Create(temp.File("session.jsonl"), "/work");
        AddUsage(session, "fixture-unpriced", "chat", "chat", "primary", "fixture", 1000);

        UsageCostReport report = Assert.IsType<UsageCostReport>(
            UsageCost.SummarizeAttributedSession(catalog, session)
        );

        Assert.True(report.HasUnpriced);
        Assert.Null(Assert.Single(report.ByUsage).Cost);
        Assert.Null(report.Total);
    }

    [Fact]
    public void Summarize_usage_events_falls_back_to_the_session_model()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(temp, PricedModel);

        CostReport report = Assert.IsType<CostReport>(
            UsageCost.Summarize(
                catalog,
                [new UsageUpdated("run_1", Details(1000, 500))],
                "fixture-priced"
            )
        );

        Assert.Equal(0.0075m, report.Total);
    }

    [Fact]
    public void Summarize_usage_events_prefers_the_event_model_id()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(temp, PricedModel, OtherPricedModel);

        CostReport report = Assert.IsType<CostReport>(
            UsageCost.Summarize(
                catalog,
                [new UsageUpdated("run_1", Details(1000, 500)) { ModelId = "fixture-other" }],
                "fixture-priced"
            )
        );

        Assert.Equal("fixture-other", Assert.Single(report.Models).ModelId);
        Assert.Equal(0.002m, report.Total);
    }

    [Fact]
    public void Summarize_keeps_the_decimal_math_under_a_comma_decimal_culture()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(temp, PricedModel);
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-AT");

            CostReport report = Assert.IsType<CostReport>(
                UsageCost.Summarize(
                    catalog,
                    [("fixture-priced", new UsageLanes(1_000_000, 10_000, 200_000, 100_000))]
                )
            );

            Assert.Equal(2.475m, report.Total);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static ModelCatalog Catalog(TempDirectory temp, params string[] models)
    {
        string path = temp.File("models.json");
        File.WriteAllText(
            path,
            $$"""{ "schemaVersion": 2, "models": [ {{string.Join(",", models)}} ] }"""
        );
        return ModelCatalog.Load(path);
    }

    private static UsageDetails Details(long input, long output) =>
        new() { InputTokenCount = input, OutputTokenCount = output };

    private static void AddUsage(
        Session session,
        string model,
        string usage,
        string profile,
        string provider,
        string providerKind,
        int input
    ) =>
        session.AppendMessage(
            new ChatMessage(ChatRole.Assistant, "call"),
            model,
            new SessionUsage(input, 0)
            {
                Attribution = new ModelUsageAttribution(usage, profile, provider, providerKind),
            }
        );
}

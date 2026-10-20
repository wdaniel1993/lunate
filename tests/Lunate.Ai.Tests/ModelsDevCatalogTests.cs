using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lunate.Ai.Tests;

public sealed class ModelsDevCatalogTests
{
    [Fact]
    public void Generate_maps_a_models_dev_entry_to_catalog_fields()
    {
        ModelInfo model = Find("claude-sonnet-5-5");

        Assert.Equal("anthropic", model.Provider);
        Assert.Null(model.Endpoint);
        Assert.Equal(1_000_000, model.ContextWindow);
        Assert.Equal(128_000, model.MaxOutput);
        Assert.True(model.SupportsTools);
        Assert.True(model.SupportsVision);
        Assert.Equal(new ModelPricing(2m, 10m, 0.1m, 2.5m), model.Pricing);
    }

    [Fact]
    public void Generate_keeps_a_native_id_bare()
    {
        CatalogSnapshot snapshot = Generate();

        ModelInfo model = snapshot.Models.Single(candidate => candidate.Id == "gpt-4o");
        Assert.Equal("openai", model.Provider);
        Assert.Null(model.WireId);
        Assert.DoesNotContain(snapshot.Models, candidate => candidate.Id == "openai/gpt-4o");
    }

    [Fact]
    public void Generate_qualifies_a_non_native_id()
    {
        ModelInfo model = Generate()
            .Models.Single(candidate => candidate.Id == "openrouter/anthropic/claude-sonnet-5-5");

        Assert.Equal("openrouter", model.Provider);
        Assert.Equal("anthropic/claude-sonnet-5-5", model.WireId);
    }

    [Fact]
    public void Generate_uses_budget_for_an_anthropic_reasoning_model()
    {
        Assert.Equal("budget", Find("claude-sonnet-5-5").Reasoning);
    }

    [Fact]
    public void Generate_uses_effort_for_an_openai_compatible_reasoning_model()
    {
        Assert.Equal("effort", Find("openrouter/anthropic/claude-sonnet-5-5").Reasoning);
    }

    [Fact]
    public void Generate_uses_effort_for_a_native_openai_reasoning_model()
    {
        Assert.Equal("effort", Find("gpt-5").Reasoning);
    }

    [Fact]
    public void Generate_leaves_reasoning_null_when_the_model_does_not_reason()
    {
        Assert.Null(Find("gpt-4o").Reasoning);
    }

    [Fact]
    public void Generate_defaults_a_missing_limit_to_zero()
    {
        ModelInfo model = Find("no-limits");

        Assert.Equal(0, model.ContextWindow);
        Assert.Null(model.MaxOutput);
    }

    [Fact]
    public void Generate_orders_models_by_preset_then_model_key()
    {
        string[] expected =
        [
            "gpt-4o",
            "gpt-4o-mini",
            "gpt-5",
            "no-limits",
            "claude-sonnet-5-5",
            "openrouter/anthropic/claude-sonnet-5-5",
        ];

        Assert.Equal(expected, Generate().Models.Select(model => model.Id));
    }

    [Fact]
    public void Generate_is_byte_deterministic_across_runs()
    {
        CatalogSnapshot first = Generate();
        CatalogSnapshot second = Generate();

        Assert.Equal(first.Json, second.Json);
        Assert.Equal(first.SourceSha256, second.SourceSha256);
    }

    [Fact]
    public void Generate_is_byte_deterministic_under_de_AT_culture()
    {
        string expected = Generate().Json;
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-AT");

            Assert.Equal(expected, Generate().Json);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Generate_writes_the_source_object_with_the_input_sha256()
    {
        string json = SnapshotJson();
        string expectedHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(json))
        );

        CatalogSnapshot snapshot = ModelsDevCatalog.Generate(json, ProviderPresets.All);

        Assert.Equal(expectedHash, snapshot.SourceSha256);
        using JsonDocument document = JsonDocument.Parse(snapshot.Json);
        JsonElement source = document.RootElement.GetProperty("source");
        Assert.Equal("models.dev", source.GetProperty("name").GetString());
        Assert.Equal("https://models.dev/api.json", source.GetProperty("url").GetString());
        Assert.Equal(expectedHash, source.GetProperty("sha256").GetString());
        Assert.DoesNotContain("timestamp", snapshot.Json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_writes_one_model_object_per_line()
    {
        CatalogSnapshot snapshot = Generate();

        using JsonDocument document = JsonDocument.Parse(snapshot.Json);
        string[] lines = snapshot.Json.Split('\n');
        Assert.Equal("{", lines[0]);
        Assert.Equal("  \"schemaVersion\": 2,", lines[1]);
        Assert.StartsWith("  \"source\": {", lines[2], StringComparison.Ordinal);
        Assert.Equal("  \"models\": [", lines[3]);
        for (int index = 4; index < 4 + snapshot.Models.Count; index++)
        {
            Assert.StartsWith("    {", lines[index], StringComparison.Ordinal);
            Assert.Contains("\"id\":", lines[index], StringComparison.Ordinal);
        }

        Assert.Equal("  ]", lines[4 + snapshot.Models.Count]);
        Assert.Equal("}", lines[5 + snapshot.Models.Count]);
        Assert.Equal(7 + snapshot.Models.Count, lines.Length);
    }

    [Fact]
    public void Generate_ends_with_one_newline()
    {
        string json = Generate().Json;

        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("}\n\n", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_omits_a_null_price_rate()
    {
        string line = JsonLine("gpt-4o");

        Assert.Contains(
            "\"pricing\":{\"input\":2.5,\"output\":10,\"cacheRead\":1.25}",
            line,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("cacheWrite", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_omits_pricing_when_the_snapshot_has_no_cost()
    {
        Assert.Null(Find("gpt-5").Pricing);
        Assert.DoesNotContain("pricing", JsonLine("gpt-5"), StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_omits_absent_optional_metadata()
    {
        string line = JsonLine("gpt-4o");

        Assert.DoesNotContain("wireId", line, StringComparison.Ordinal);
        Assert.DoesNotContain("reasoning", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_omits_false_supportsVision()
    {
        Assert.DoesNotContain("supportsVision", JsonLine("gpt-5"), StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_with_missing_preset_provider_throws_naming_it()
    {
        string json = """{ "openai": { "models": {} } }""";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ModelsDevCatalog.Generate(json, TwoPresets())
        );

        Assert.Contains("anthropic", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_with_provider_without_models_object_throws_naming_it()
    {
        string json = """{ "openai": { "id": "openai" } }""";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ModelsDevCatalog.Generate(json, TwoPresets())
        );

        Assert.Contains("openai", exception.Message, StringComparison.Ordinal);
        Assert.Contains("models", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_with_malformed_json_throws_naming_the_source()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ModelsDevCatalog.Generate("{", ProviderPresets.All)
        );

        Assert.Contains("models.dev", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_with_duplicate_catalog_id_throws_naming_the_id()
    {
        ProviderPreset openai = ProviderPresets.Find("openai")!;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ModelsDevCatalog.Generate(SnapshotJson(), [openai, openai])
        );

        Assert.Contains("gpt-4o", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateUniqueWireIds_with_duplicate_in_one_provider_throws_naming_it()
    {
        ModelInfo[] models =
        [
            WireModel("openrouter/a", "openrouter", "shared"),
            WireModel("openrouter/b", "openrouter", "shared"),
        ];

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ModelsDevCatalog.ValidateUniqueWireIds(models)
        );

        Assert.Contains("shared", exception.Message, StringComparison.Ordinal);
        Assert.Contains("openrouter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateUniqueWireIds_allows_the_same_wire_id_under_different_providers()
    {
        ModelInfo[] models =
        [
            WireModel("openrouter/a", "openrouter", "shared"),
            WireModel("deepseek/a", "deepseek", "shared"),
        ];

        ModelsDevCatalog.ValidateUniqueWireIds(models);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Generate_without_json_throws_argument_exception(string? json)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ModelsDevCatalog.Generate(json!, ProviderPresets.All)
        );
    }

    [Fact]
    public void Generate_without_presets_throws_argument_exception()
    {
        Assert.ThrowsAny<ArgumentException>(() => ModelsDevCatalog.Generate("{}", null!));
        Assert.ThrowsAny<ArgumentException>(() => ModelsDevCatalog.Generate("{}", []));
    }

    private static CatalogSnapshot Generate() =>
        ModelsDevCatalog.Generate(SnapshotJson(), ProviderPresets.All);

    private static ModelInfo Find(string id) => Generate().Models.Single(model => model.Id == id);

    private static string JsonLine(string id) =>
        Generate()
            .Json.Split('\n')
            .Single(line => line.Contains($"\"id\":\"{id}\"", StringComparison.Ordinal));

    private static string SnapshotJson() =>
        File.ReadAllText(
            Path.Combine(
                TestPaths.FindRepositoryRoot(),
                "tests",
                "fixtures",
                "models-dev",
                "sample.json"
            )
        );

    private static IReadOnlyList<ProviderPreset> TwoPresets() =>
        [ProviderPresets.Find("openai")!, ProviderPresets.Find("anthropic")!];

    private static ModelInfo WireModel(string id, string provider, string wireId) =>
        new(id, provider, null, 1, true) { WireId = wireId };
}

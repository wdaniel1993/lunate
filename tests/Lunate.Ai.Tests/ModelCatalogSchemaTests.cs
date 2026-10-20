namespace Lunate.Ai.Tests;

public sealed class ModelCatalogSchemaTests
{
    [Fact]
    public void Load_schema_1_entry_keeps_the_existing_fields_and_has_no_schema_2_metadata()
    {
        using var temp = new TempDirectory();
        string userFile = WriteJson(
            temp.File("models.json"),
            """
            {
              "schemaVersion": 1,
              "models": [
                { "id": "legacy-model", "provider": "groq", "endpoint": null, "contextWindow": 32000, "supportsTools": true, "reasoning": "effort" }
              ]
            }
            """
        );

        ModelCatalog catalog = ModelCatalog.Load(userFile);

        ModelInfo model = catalog.Find("legacy-model")!;
        Assert.Equal("legacy-model", model.Id);
        Assert.Equal("groq", model.Provider);
        Assert.Equal(32_000, model.ContextWindow);
        Assert.True(model.SupportsTools);
        Assert.Equal("effort", model.Reasoning);
        Assert.Null(model.Pricing);
        Assert.Null(model.MaxOutput);
        Assert.False(model.SupportsVision);
        Assert.Null(model.WireId);
    }

    [Fact]
    public void Load_schema_2_entry_reads_pricing_and_capabilities()
    {
        using var temp = new TempDirectory();
        string userFile = WriteJson(
            temp.File("models.json"),
            """
            {
              "schemaVersion": 2,
              "models": [
                {
                  "id": "openrouter/anthropic/claude-sonnet-5-5",
                  "provider": "openrouter",
                  "contextWindow": 200000,
                  "supportsTools": true,
                  "maxOutput": 64000,
                  "supportsVision": true,
                  "wireId": "anthropic/claude-sonnet-5-5",
                  "pricing": { "input": 3, "output": 15, "cacheRead": 0.3, "cacheWrite": 3.75 }
                }
              ]
            }
            """
        );

        ModelCatalog catalog = ModelCatalog.Load(userFile);

        ModelInfo model = catalog.Find("openrouter/anthropic/claude-sonnet-5-5")!;
        Assert.Equal("openrouter", model.Provider);
        Assert.Equal(200_000, model.ContextWindow);
        Assert.True(model.SupportsTools);
        Assert.Equal(64_000, model.MaxOutput);
        Assert.True(model.SupportsVision);
        Assert.Equal("anthropic/claude-sonnet-5-5", model.WireId);
        Assert.Equal(new ModelPricing(3m, 15m, 0.3m, 3.75m), model.Pricing);
    }

    [Fact]
    public void Load_entry_without_pricing_is_unpriced()
    {
        using var temp = new TempDirectory();
        string userFile = WriteJson(
            temp.File("models.json"),
            """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "unpriced-model", "provider": "groq", "contextWindow": 1000, "supportsTools": true }
              ]
            }
            """
        );

        ModelCatalog catalog = ModelCatalog.Load(userFile);

        Assert.Null(catalog.Find("unpriced-model")!.Pricing);
    }

    [Fact]
    public void LoadLayered_merges_the_managed_cache_below_the_user_file()
    {
        using var temp = new TempDirectory();
        string managedFile = WriteJson(
            temp.File("models.catalog.json"),
            """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "gpt-4o-mini", "provider": "openai", "contextWindow": 111, "supportsTools": true },
                { "id": "managed-only", "provider": "groq", "contextWindow": 222, "supportsTools": true }
              ]
            }
            """
        );
        string userFile = WriteJson(
            temp.File("models.json"),
            """
            {
              "schemaVersion": 1,
              "models": [
                { "id": "gpt-4o-mini", "provider": "openai", "contextWindow": 333, "supportsTools": true },
                { "id": "user-only", "provider": "openai", "contextWindow": 444, "supportsTools": true }
              ]
            }
            """
        );

        ModelCatalog catalog = ModelCatalog.LoadLayered(managedFile, userFile);

        Assert.Equal(333, catalog.Find("gpt-4o-mini")!.ContextWindow);
        Assert.Equal(222, catalog.Find("managed-only")!.ContextWindow);
        Assert.Equal(444, catalog.Find("user-only")!.ContextWindow);
        Assert.Equal(6, catalog.Models.Count);
        Assert.Equal("gpt-4o-mini", catalog.Models[0].Id);
        Assert.Equal("managed-only", catalog.Models[4].Id);
        Assert.Equal("user-only", catalog.Models[5].Id);
    }

    private static string WriteJson(string path, string json)
    {
        File.WriteAllText(path, json);
        return path;
    }
}

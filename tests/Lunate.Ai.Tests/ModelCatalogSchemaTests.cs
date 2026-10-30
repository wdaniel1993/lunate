namespace Lunate.Ai.Tests;

public sealed class ModelCatalogSchemaTests
{
    [Fact]
    public void Load_schema_2_entry_reads_open_model_capability_sets()
    {
        using var temp = new TempDirectory();
        string userFile = WriteJson(
            temp.File("models.json"),
            """
            {
              "schemaVersion": 2,
              "models": [
                {
                  "id": "open-capability-model",
                  "provider": "openrouter",
                  "contextWindow": 32000,
                  "supportsTools": true,
                  "capabilities": {
                    "apis": ["chat", "future-api"],
                    "inputModalities": ["text", "image"],
                    "outputModalities": ["text"],
                    "features": ["tools", "structured-output"]
                  }
                }
              ]
            }
            """
        );

        ModelCatalog catalog = ModelCatalog.Load(userFile);

        ModelCapabilityDescriptor capabilities = catalog
            .Find("open-capability-model")!
            .Capabilities!;
        Assert.Contains("future-api", capabilities.Apis!);
        Assert.Contains("image", capabilities.InputModalities!);
        Assert.Contains("text", capabilities.OutputModalities!);
        Assert.Contains("structured-output", capabilities.Features!);
    }

    [Fact]
    public void Load_schema_2_entry_rejects_unknown_api_type_label()
    {
        using var temp = new TempDirectory();
        string userFile = WriteJson(
            temp.File("models.json"),
            """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "invalid-api-type", "provider": "openai", "contextWindow": 32000,
                  "supportsTools": true, "apiType": "cannot-decide" }
              ]
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ModelCatalog.Load(userFile)
        );

        Assert.Contains("apiType", exception.Message, StringComparison.Ordinal);
        Assert.Contains("native", exception.Message, StringComparison.Ordinal);
        Assert.Contains("adapter", exception.Message, StringComparison.Ordinal);
    }

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
    public void Load_a_schema_1_override_keeps_the_embedded_price_and_metadata()
    {
        using var temp = new TempDirectory();
        string userFile = WriteJson(
            temp.File("models.json"),
            """
            {
              "schemaVersion": 1,
              "models": [
                {
                  "id": "gpt-4o-mini",
                  "provider": "openai",
                  "endpoint": "https://example.test/v1",
                  "contextWindow": 2000,
                  "supportsTools": false,
                  "auth": "custom-key",
                  "reasoning": "effort"
                },
                {
                  "id": "openrouter/openai/gpt-4o-mini",
                  "provider": "openrouter",
                  "contextWindow": 3000,
                  "supportsTools": true
                }
              ]
            }
            """
        );

        ModelCatalog catalog = ModelCatalog.Load(userFile);

        ModelInfo native = catalog.Find("gpt-4o-mini")!;
        Assert.Equal(new Uri("https://example.test/v1"), native.Endpoint);
        Assert.Equal(2000, native.ContextWindow);
        Assert.False(native.SupportsTools);
        Assert.Equal("custom-key", native.AuthRef);
        Assert.Equal("effort", native.Reasoning);
        Assert.NotNull(native.Pricing);
        Assert.Equal(16384, native.MaxOutput);
        Assert.True(native.SupportsVision);

        ModelInfo qualified = catalog.Find("openrouter/openai/gpt-4o-mini")!;
        Assert.Equal("openai/gpt-4o-mini", qualified.WireId);
        Assert.Equal(3000, qualified.ContextWindow);
    }

    [Fact]
    public void Load_a_schema_2_override_replaces_the_embedded_entry_completely()
    {
        using var temp = new TempDirectory();
        string userFile = WriteJson(
            temp.File("models.json"),
            """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "gpt-4o-mini", "provider": "openai", "contextWindow": 2000, "supportsTools": true }
              ]
            }
            """
        );

        ModelCatalog catalog = ModelCatalog.Load(userFile);

        ModelInfo model = catalog.Find("gpt-4o-mini")!;
        Assert.Equal(2000, model.ContextWindow);
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

        IReadOnlyList<ModelInfo> embedded = ModelCatalog.LoadLayered(null, null).Models;
        int builtInPosition = embedded.ToList().FindIndex(model => model.Id == "gpt-4o-mini");
        Assert.Equal(333, catalog.Find("gpt-4o-mini")!.ContextWindow);
        Assert.Equal(222, catalog.Find("managed-only")!.ContextWindow);
        Assert.Equal(444, catalog.Find("user-only")!.ContextWindow);
        Assert.Equal(embedded.Count + 2, catalog.Models.Count);
        Assert.Equal("gpt-4o-mini", catalog.Models[builtInPosition].Id);
        Assert.Equal("managed-only", catalog.Models[embedded.Count].Id);
        Assert.Equal("user-only", catalog.Models[embedded.Count + 1].Id);
    }

    [Fact]
    public void LoadLayered_lets_the_managed_cache_override_the_embedded_catalog()
    {
        using var temp = new TempDirectory();
        string managedFile = WriteJson(
            temp.File("models.catalog.json"),
            """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "gpt-4o-mini", "provider": "openai", "contextWindow": 111, "supportsTools": true }
              ]
            }
            """
        );

        ModelCatalog catalog = ModelCatalog.LoadLayered(managedFile, userFilePath: null);

        Assert.Equal(111, catalog.Find("gpt-4o-mini")!.ContextWindow);
    }

    private static string WriteJson(string path, string json)
    {
        File.WriteAllText(path, json);
        return path;
    }
}

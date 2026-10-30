using System.Text.Json;

namespace Lunate.Ai.Tests;

public sealed class ModelProfileResolutionTests
{
    [Fact]
    public void Resolves_profile_provider_model_and_validated_options()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(
            temp,
            """
            { "id": "test-model", "provider": "openai", "contextWindow": 128000,
              "supportsTools": true, "reasoning": "effort", "maxOutput": 8192,
              "supportsTemperature": true }
            """
        );
        ProviderEntry provider = Provider("work-openai", "https://work.example.test/v1");
        var profile = new ModelProfile(
            "chat-default",
            "work-openai",
            "test-model",
            ReasoningLevel: "medium",
            MaxOutput: 4096,
            Temperature: 0.4m,
            ContextWindow: 64_000,
            DecisionThreshold: 0.75
        );

        ResolvedModelProfile result = ModelProfileResolver.Resolve(profile, [provider], catalog);

        Assert.Equal("chat-default", result.Profile.Name);
        Assert.Equal(provider, result.Provider);
        Assert.Equal("test-model", result.Model.Id);
        Assert.Equal(64_000, result.ContextWindow);
        Assert.Equal(4096, result.Profile.MaxOutput);
        Assert.Equal(0.4m, result.Profile.Temperature);
    }

    [Fact]
    public void Accepts_a_supported_reasoning_budget_within_the_model_output_limit()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(
            temp,
            """
            { "id": "budget-model", "provider": "openai", "contextWindow": 128000,
              "supportsTools": true, "reasoning": "budget", "maxOutput": 8192 }
            """
        );
        var profile = new ModelProfile(
            "budget-profile",
            "work-openai",
            "budget-model",
            ReasoningBudget: 4096
        );

        ResolvedModelProfile resolved = ModelProfileResolver.Resolve(
            profile,
            [Provider("work-openai", "https://work.example.test/v1")],
            catalog
        );

        Assert.Equal(4096, resolved.Profile.ReasoningBudget);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9000)]
    public void Refuses_a_reasoning_budget_outside_model_output_limit(int budget)
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(
            temp,
            """
            { "id": "budget-model", "provider": "openai", "contextWindow": 128000,
              "supportsTools": true, "reasoning": "budget", "maxOutput": 8192 }
            """
        );
        var profile = new ModelProfile(
            "budget-profile",
            "work-openai",
            "budget-model",
            ReasoningBudget: budget
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ModelProfileResolver.Resolve(
                profile,
                [Provider("work-openai", "https://work.example.test/v1")],
                catalog
            )
        );

        Assert.Contains("reasoningBudget", exception.Message, StringComparison.Ordinal);
        Assert.Contains("budget-profile", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("reasoningLevel")]
    [InlineData("reasoningBudget")]
    [InlineData("maxOutput")]
    [InlineData("temperature")]
    [InlineData("contextWindow")]
    [InlineData("decisionThreshold")]
    public void Unsupported_profile_option_names_the_field_and_profile(string field)
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(
            temp,
            """
            { "id": "limited-model", "provider": "openai", "contextWindow": 128000,
              "supportsTools": true, "maxOutput": 4096 }
            """
        );
        ProviderEntry provider = Provider("work-openai", "https://work.example.test/v1");
        ModelProfile profile = UnsupportedProfile(field);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ModelProfileResolver.Resolve(profile, [provider], catalog)
        );

        Assert.Contains(field, exception.Message, StringComparison.Ordinal);
        Assert.Contains(profile.Name, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("request", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ModelProfile UnsupportedProfile(string field)
    {
        var profile = new ModelProfile("unsupported-profile", "work-openai", "limited-model");
        return field switch
        {
            "reasoningLevel" => profile with { ReasoningLevel = "high" },
            "reasoningBudget" => profile with { ReasoningBudget = 2048 },
            "maxOutput" => profile with { MaxOutput = 8192 },
            "temperature" => profile with { Temperature = 0.4m },
            "contextWindow" => profile with { ContextWindow = 256_000 },
            "decisionThreshold" => profile with { DecisionThreshold = 1.1 },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    private static ModelCatalog Catalog(TempDirectory temp, string model)
    {
        string path = temp.File("models.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                new { schemaVersion = 2, models = new[] { JsonDocument.Parse(model).RootElement } }
            )
        );
        return ModelCatalog.Load(path);
    }

    private static ProviderEntry Provider(string name, string endpoint)
    {
        bool valid = ProviderEntry.TryCreate(
            name,
            "openai",
            new Uri(endpoint),
            "auth:openai",
            headers: null,
            region: null,
            out ProviderEntry? provider,
            out string? error
        );

        Assert.True(valid, error);
        return Assert.IsType<ProviderEntry>(provider);
    }
}

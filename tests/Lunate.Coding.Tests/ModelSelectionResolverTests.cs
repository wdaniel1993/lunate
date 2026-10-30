using System.Text.Json;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

public sealed class ModelSelectionResolverTests
{
    [Fact]
    public void Selecting_a_chat_profile_preserves_other_usage_assignments()
    {
        AgentSettings settings = Settings(
            profiles: ["chat-a", "chat-b", "fast", "decision", "subagents", "compaction"],
            usages: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["chat"] = "chat-a",
                ["fast"] = "fast",
                ["decision"] = "decision",
                ["subagents"] = "subagents",
                ["compaction"] = "compaction",
            }
        );

        var provider = new ProviderEntry(
            "provider",
            "openai",
            new Uri("https://provider.example.test/v1"),
            "auth:provider"
        );
        var model = new ModelInfo("model", "openai", null, 64000, SupportsTools: true);
        var selectedProfile = new ModelProfile("selected-chat", "provider", "model");
        var resolved = new ResolvedModelProfile(
            selectedProfile,
            provider,
            model,
            model.ContextWindow,
            new ModelCapabilityDescriptor()
        );

        AgentSettings selected = ModelSelectionResolver.SelectChatProfile(settings, resolved);

        Assert.Equal("selected-chat", selected.Usages["chat"]);
        Assert.Contains("selected-chat", selected.Profiles.Keys);
        Assert.Equal("fast", selected.Usages["fast"]);
        Assert.Equal("decision", selected.Usages["decision"]);
        Assert.Equal("subagents", selected.Usages["subagents"]);
        Assert.Equal("compaction", selected.Usages["compaction"]);
    }

    [Fact]
    public void Qualified_provider_model_uses_the_provider_entry_and_model_defaults()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(
            temp,
            """
            { "id": "openrouter/vendor/model-x", "provider": "openrouter",
              "contextWindow": 64000, "supportsTools": true, "maxOutput": 8192 }
            """
        );
        ProviderEntry provider = Provider("router", "openrouter", "https://router.example.test/v1");

        ResolvedModelProfile resolved = ModelSelectionResolver.ResolveChatModel(
            "router/vendor/model-x",
            new AgentSettings(),
            [provider],
            catalog
        );

        Assert.Equal("router", resolved.Profile.Provider);
        Assert.Equal("openrouter/vendor/model-x", resolved.Model.Id);
        Assert.Equal(new Uri("https://router.example.test/v1"), resolved.Provider.BaseUrl);
        Assert.Equal(new Uri("https://router.example.test/v1"), resolved.Model.Endpoint);
        Assert.Null(resolved.Profile.MaxOutput);
        Assert.Equal(8192, resolved.Model.MaxOutput);
    }

    [Fact]
    public void Unique_bare_model_id_uses_its_configured_provider()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(
            temp,
            """
            { "id": "openrouter/vendor/model-x", "provider": "openrouter",
              "wireId": "vendor/model-x", "contextWindow": 64000, "supportsTools": true }
            """
        );
        ProviderEntry provider = Provider(
            "gateway",
            "openrouter",
            "https://router.example.test/v1"
        );

        ResolvedModelProfile resolved = ModelSelectionResolver.ResolveChatModel(
            "model-x",
            new AgentSettings(),
            [provider],
            catalog
        );

        Assert.Equal("openrouter/vendor/model-x", resolved.Model.Id);
        Assert.Equal("gateway", resolved.Provider.Name);
    }

    [Fact]
    public void Ambiguous_bare_model_id_names_every_candidate()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(
            temp,
            """
            { "id": "openrouter/vendor-a/shared", "provider": "openrouter",
              "wireId": "shared", "contextWindow": 64000, "supportsTools": true }
            """,
            """
            { "id": "google/vendor-b/shared", "provider": "google",
              "wireId": "shared", "contextWindow": 64000, "supportsTools": true }
            """
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ModelSelectionResolver.ResolveChatModel("shared", new AgentSettings(), [], catalog)
        );

        Assert.Contains("openrouter/vendor-a/shared", exception.Message, StringComparison.Ordinal);
        Assert.Contains("google/vendor-b/shared", exception.Message, StringComparison.Ordinal);
        Assert.Contains("qualified id", exception.Message, StringComparison.Ordinal);
    }

    private static AgentSettings Settings(
        IReadOnlyList<string> profiles,
        IReadOnlyDictionary<string, string> usages
    ) =>
        new()
        {
            Profiles = profiles.ToDictionary(
                static name => name,
                static name => new ModelProfile(name, "provider", "model"),
                StringComparer.Ordinal
            ),
            Usages = usages,
        };

    private static ModelCatalog Catalog(TempDirectory temp, params string[] models)
    {
        string path = temp.File("models.json");
        JsonElement[] entries = models
            .Select(model =>
            {
                using JsonDocument document = JsonDocument.Parse(model);
                return document.RootElement.Clone();
            })
            .ToArray();
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(new { schemaVersion = 2, models = entries })
        );
        return ModelCatalog.Load(path);
    }

    private static ProviderEntry Provider(string name, string kind, string endpoint)
    {
        bool valid = ProviderEntry.TryCreate(
            name,
            kind,
            new Uri(endpoint),
            $"auth:{name}",
            headers: null,
            region: null,
            out ProviderEntry? provider,
            out string? error
        );

        Assert.True(valid, error);
        return Assert.IsType<ProviderEntry>(provider);
    }
}

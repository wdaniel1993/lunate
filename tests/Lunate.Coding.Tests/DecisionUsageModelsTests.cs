using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class DecisionUsageModelsTests
{
    [Fact]
    public async Task Decision_profile_uses_provider_alias_environment_credential()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("models.json"),
            """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "gemini-test", "provider": "google", "contextWindow": 64000,
                  "supportsTools": false }
              ]
            }
            """
        );
        bool valid = ProviderEntry.TryCreate(
            "google-provider",
            "google",
            new Uri("https://provider.example.test/v1"),
            "auth:google-key",
            headers: null,
            region: null,
            out ProviderEntry? provider,
            out string? error
        );
        Assert.True(valid, error);

        var settings = new AgentSettings
        {
            Decisions = new DecisionsSettings { Profile = "decision-profile" },
            Profiles = new Dictionary<string, ModelProfile>(StringComparer.Ordinal)
            {
                ["decision-profile"] = new("decision-profile", "google-provider", "gemini-test"),
            },
        };
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        var factory = new RecordingFactory(client);
        UsageModelClient decision = Assert.IsType<UsageModelClient>(
            DecisionUsageModels.ResolveDecisionModel(
                settings,
                () => (AuthStore.Load(environment: _ => null), [provider!]),
                ModelCatalog.Load(temp.File("models.json")),
                name => name == "GEMINI_API_KEY" ? "alias-key" : null,
                factory,
                _ => { },
                []
            )
        );

        await decision.Client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal("alias-key", factory.ApiKey);
    }

    private sealed class RecordingFactory(IChatClient client) : IChatClientFactory
    {
        public string? ApiKey { get; private set; }

        public IChatClient Create(ModelInfo model) => client;

        public IChatClient Create(ModelInfo model, string? apiKey)
        {
            ApiKey = apiKey;
            return client;
        }
    }
}

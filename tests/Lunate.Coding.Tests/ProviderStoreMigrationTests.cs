using System.Text.Json;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

public sealed class ProviderStoreMigrationTests
{
    [Fact]
    public void Legacy_auth_keys_migrate_idempotently_without_replacing_custom_endpoints_or_secrets()
    {
        using var temp = new TempDirectory();
        string authPath = temp.File("auth.json");
        string settingsPath = temp.File("settings.json");
        string providerStorePath = temp.File("providers.json");
        File.Copy(Fixture("legacy-auth.json"), authPath);
        File.Copy(Fixture("legacy-settings.json"), settingsPath);

        AuthStore auth = AuthStore.Load(authPath, _ => null);
        AgentSettings settings = SettingsStore.Resolve(settingsPath, _ => null);

        ProviderStore providerStore = ProviderStore.Load(
            providerStorePath,
            auth,
            settings.Providers
        );
        ProviderStore reloaded = ProviderStore.Load(providerStorePath, auth, settings.Providers);

        ProviderEntry openAi = Assert.Single(reloaded.Entries, entry => entry.Name == "openai");
        ProviderEntry local = Assert.Single(reloaded.Entries, entry => entry.Name == "local");
        Assert.Equal("openai", openAi.Kind);
        Assert.Equal("auth:openai", openAi.CredentialReference.ToString());
        Assert.Equal(new Uri("https://custom.example.test/v1"), local.BaseUrl);
        Assert.Equal("auth:local", local.CredentialReference.ToString());
        Assert.Equal(2, reloaded.Entries.Count);

        string persisted = File.ReadAllText(providerStorePath);
        Assert.DoesNotContain("fixture-only-openai-key", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-only-local-key", persisted, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(persisted);
        Assert.Equal(2, document.RootElement.GetProperty("providers").GetArrayLength());
    }

    [Fact]
    public void Environment_credentials_still_override_migrated_stored_keys()
    {
        using var temp = new TempDirectory();
        string authPath = temp.File("auth.json");
        File.Copy(Fixture("legacy-auth.json"), authPath);

        AuthStore auth = AuthStore.Load(
            authPath,
            name => name == "OPENAI_API_KEY" ? "ci-openai-key" : null
        );

        Assert.Equal("ci-openai-key", auth.TryGet("openai"));
        Assert.Equal("fixture-only-openai-key", auth.Keys["openai"]);
        string providerSettings = File.ReadAllText(temp.File("providers.json"));
        Assert.DoesNotContain("ci-openai-key", providerSettings, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "fixture-only-openai-key",
            providerSettings,
            StringComparison.Ordinal
        );
    }

    private static string Fixture(string name) =>
        Path.Combine(RepositoryRoot.Find(), "tests", "fixtures", "provider-store", name);
}

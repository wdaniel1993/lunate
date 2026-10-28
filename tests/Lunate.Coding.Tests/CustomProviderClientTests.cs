using Lunate.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lunate.Coding.Tests;

public sealed class CustomProviderClientTests
{
    private const string Secret = "sk-custom-provider-secret";

    [Fact]
    public void A_stored_provider_builds_a_client_with_its_endpoint_and_key()
    {
        using var temp = new TempDirectory();
        string settingsPath = temp.File("settings.json");
        string authPath = temp.File("auth.json");
        string modelsPath = temp.File("models.json");
        var provider = new CustomProvider(
            "local",
            new Uri("http://127.0.0.1:11434/v1"),
            "qwen2.5-coder",
            "local/qwen2.5-coder"
        );
        SettingsStore.WriteProvider(provider, settingsPath);
        AuthStore auth = AuthStore.Load(authPath, _ => null);
        auth.Set(provider.Id, Secret);
        auth.Save();

        AgentSettings settings = SettingsStore.Resolve(settingsPath, _ => null);
        ModelCatalog catalog = HarnessFactory.LoadCatalog(settings, modelsPath);
        ModelInfo model =
            catalog.Find(provider.CatalogName) ?? throw new InvalidOperationException("not found");
        var factory = new ChatClientFactory(
            NullLoggerFactory.Instance,
            enableOpenTelemetry: false,
            namedKeySource: auth.TryGet
        );

        IChatClient client = factory.Create(model);

        Assert.NotNull(client);
        Assert.Equal(provider.BaseUrl, model.Endpoint);
        Assert.Equal(provider.Id, model.AuthRef);
        Assert.Equal(provider.Model, model.WireId);
        Assert.Equal(
            Secret,
            ChatClientFactory.ResolveApiKey(model, ProviderPresets.Find("openai")!, auth.TryGet)
        );
        Assert.DoesNotContain(Secret, File.ReadAllText(settingsPath), StringComparison.Ordinal);
        Assert.Contains(Secret, File.ReadAllText(authPath), StringComparison.Ordinal);
    }
}

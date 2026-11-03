using System.Text.Json;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

public sealed class ProviderStoreSettingsTests
{
    [Fact]
    public void Provider_settings_round_trip_references_without_persisting_secret_values()
    {
        using var temp = new TempDirectory();
        string path = temp.File("providers.json");
        string authPath = temp.File("auth.json");
        const string credentialSecret = "fixture-only-provider-secret";
        const string headerSecret = "fixture-only-header-secret";
        AuthStore auth = AuthStore.Load(authPath, _ => null);
        auth.Set("work", credentialSecret);
        auth.Set("workspace-header", headerSecret);
        auth.Save();
        ProviderEntry first = CreateEntry(
            "work-openai",
            new Uri("https://work.example.test/v1"),
            "auth:work",
            new Dictionary<string, string> { ["X-Workspace"] = "auth:workspace-header" },
            "eu"
        );
        ProviderEntry second = CreateEntry(
            "personal-openai",
            new Uri("https://personal.example.test/v1"),
            "env:PERSONAL_OPENAI_KEY",
            new Dictionary<string, string> { ["X-Region"] = "helper:region-header" },
            "local"
        );

        ProviderStore.Save(path, [first, second]);
        ProviderStore store = ProviderStore.Load(path, auth, []);

        Assert.Equal(2, store.Entries.Count);
        Assert.Equal(
            "openai",
            Assert.Single(store.Entries, entry => entry.Name == "work-openai").Kind
        );
        Assert.Equal(
            "auth:workspace-header",
            Assert.Single(store.Entries, entry => entry.Name == "work-openai").Headers![
                "X-Workspace"
            ]
        );
        Assert.Equal(
            "helper:region-header",
            Assert.Single(store.Entries, entry => entry.Name == "personal-openai").Headers![
                "X-Region"
            ]
        );
        string persisted = File.ReadAllText(path);
        Assert.DoesNotContain(credentialSecret, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(headerSecret, persisted, StringComparison.Ordinal);
        Assert.Contains("auth:workspace-header", persisted, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(persisted);
        Assert.Equal(2, document.RootElement.GetProperty("providers").GetArrayLength());
    }

    [Fact]
    public void A_literal_header_value_is_declared_data_and_survives_the_store()
    {
        // The header value model takes literals as declared data ("X-Title": "Lunate"); only
        // invalid values are refused, and a refusal never echoes the refused value.
        const string secret = "fixture-only-header-secret";
        using var temp = new TempDirectory();
        string path = temp.File("providers.json");
        AuthStore auth = AuthStore.Load(temp.File("auth.json"), _ => null);
        ProviderEntry entry = CreateEntry(
            "work-openai",
            new Uri("https://example.test/v1"),
            "auth:work",
            new Dictionary<string, string> { ["X-Title"] = "Lunate" },
            "eu"
        );

        ProviderStore.Save(path, [entry]);
        ProviderStore store = ProviderStore.Load(path, auth, []);

        Assert.Equal("Lunate", Assert.Single(store.Entries).Headers!["X-Title"]);

        bool invalidRefused = ProviderEntry.TryCreate(
            "work-openai",
            "openai",
            new Uri("https://example.test/v1"),
            "auth:work",
            new Dictionary<string, string> { ["X-Broken"] = secret + "\nEvil: 1" },
            region: null,
            out _,
            out string? error
        );

        Assert.False(invalidRefused);
        Assert.Contains("X-Broken", error, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_base_urls_map_and_models_url_round_trip_with_the_entry()
    {
        using var temp = new TempDirectory();
        string path = temp.File("providers.json");
        string authPath = temp.File("auth.json");
        AuthStore auth = AuthStore.Load(authPath, _ => null);
        ProviderEntry entry = CreateEntry(
            "go",
            new Uri("https://go.example.test/openai/v1"),
            "auth:go",
            (IReadOnlyDictionary<string, string>?)null,
            "eu",
            "opencode-go",
            baseUrls: new Dictionary<ProviderApiFamily, string>
            {
                [ProviderApiFamily.OpenAiChat] = "https://go.example.test/openai/v1",
                [ProviderApiFamily.OpenAiResponses] = "https://go.example.test/openai/v1",
                [ProviderApiFamily.AnthropicMessages] = "https://go.example.test/anthropic/v1",
            },
            modelsUrl: "https://go.example.test/v1/models"
        );

        ProviderStore.Save(path, [entry]);
        ProviderStore store = ProviderStore.Load(path, auth, []);

        ProviderEntry loaded = Assert.Single(store.Entries);
        Assert.Equal(
            [
                ProviderApiFamily.OpenAiChat,
                ProviderApiFamily.OpenAiResponses,
                ProviderApiFamily.AnthropicMessages,
            ],
            loaded.Families
        );
        Assert.Equal(
            new Uri("https://go.example.test/anthropic/v1"),
            loaded.UrlFor(ProviderApiFamily.AnthropicMessages)
        );
        Assert.Equal("https://go.example.test/v1/models", loaded.ModelsUrl!.AbsoluteUri);
    }

    [Fact]
    public void An_unknown_api_family_key_in_base_urls_is_refused_by_name()
    {
        using var temp = new TempDirectory();
        string path = temp.File("providers.json");
        File.WriteAllText(
            path,
            """{ "schemaVersion": 1, "providers": [ { "name": "go", "kind": "opencode-go", "baseUrl": "https://go.example.test/openai/v1", "credentialReference": "auth:go", "baseUrls": { "chat": "https://go.example.test/openai/v1" } } ] }"""
        );
        string authPath = temp.File("auth.json");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ProviderStore.Load(path, AuthStore.Load(authPath, _ => null), [])
        );

        Assert.Contains("'chat'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("supported families", exception.Message, StringComparison.Ordinal);
        Assert.Contains("providers.go.baseUrls", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_base_urls_entry_without_a_shorthand_still_loads()
    {
        using var temp = new TempDirectory();
        string path = temp.File("providers.json");
        File.WriteAllText(
            path,
            """{ "schemaVersion": 1, "providers": [ { "name": "go", "kind": "opencode-go", "credentialReference": "auth:go", "baseUrls": { "anthropic-messages": "https://go.example.test/anthropic/v1" } } ] }"""
        );
        string authPath = temp.File("auth.json");

        // Only the default family may stand in for the shorthand; 'anthropic-messages' alone with
        // no 'baseUrl' is refused, keeping the entry's meaning explicit.
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ProviderStore.Load(path, AuthStore.Load(authPath, _ => null), [])
        );

        Assert.Contains("openai-chat", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_models_url_of_the_wrong_shape_is_refused_naming_the_field()
    {
        using var temp = new TempDirectory();
        string path = temp.File("providers.json");
        File.WriteAllText(
            path,
            """{ "schemaVersion": 1, "providers": [ { "name": "go", "kind": "opencode-go", "baseUrl": "https://go.example.test/openai/v1", "credentialReference": "auth:go", "modelsUrl": "not a url" } ] }"""
        );
        string authPath = temp.File("auth.json");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ProviderStore.Load(path, AuthStore.Load(authPath, _ => null), [])
        );

        Assert.Contains("modelsUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Auth_list_never_prints_provider_or_header_secret_values()
    {
        using var temp = new TempDirectory();
        string authPath = temp.File("auth.json");
        string providerPath = temp.File("providers.json");
        const string providerSecret = "fixture-only-provider-secret";
        const string headerSecret = "fixture-only-header-secret";
        AuthStore auth = AuthStore.Load(authPath, _ => null);
        auth.Set("work-openai", providerSecret);
        auth.Set("workspace-header", headerSecret);
        auth.Save();
        ProviderStore.Save(
            providerPath,
            [
                CreateEntry(
                    "work-openai",
                    new Uri("https://work.example.test/v1"),
                    "auth:work-openai",
                    new Dictionary<string, string> { ["X-Workspace"] = "auth:workspace-header" },
                    "eu"
                ),
            ]
        );

        using var output = new StringWriter();
        using var errors = new StringWriter();
        int exit = AuthCommand.Run(
            ["list"],
            output,
            errors,
            new AuthCommandSeams(
                AuthPath: authPath,
                SettingsPath: temp.File("settings.json"),
                Environment: _ => null,
                ProviderProbeHandler: new StatusHandler()
            )
        );

        string stdout = output.ToString();
        string stderr = errors.ToString();
        Assert.Equal(0, exit);
        Assert.Contains("work-openai", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(providerSecret, stdout + stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(headerSecret, stdout + stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Provider_settings_round_trip_a_credential_placement()
    {
        using var temp = new TempDirectory();
        string path = temp.File("providers.json");
        string authPath = temp.File("auth.json");
        AuthStore auth = AuthStore.Load(authPath, _ => null);
        bool created = ProviderEntry.TryCreate(
            "go",
            "opencode-go",
            baseUrl: null,
            "auth:go",
            headers: null,
            region: null,
            baseUrls: new Dictionary<ProviderApiFamily, Uri>
            {
                [ProviderApiFamily.OpenAiChat] = new("https://go.example.test/openai/v1"),
                [ProviderApiFamily.Google] = new("https://go.example.test/google/v1beta"),
            },
            modelsUrl: null,
            new Dictionary<ProviderApiFamily, CredentialPlacement>
            {
                [ProviderApiFamily.Google] = new("key", CredentialPlacementTarget.Query),
            },
            out ProviderEntry? entry,
            out string? error
        );

        Assert.True(created, error);
        ProviderStore.Save(path, [entry!]);
        ProviderStore store = ProviderStore.Load(path, auth, []);

        // Only the declared family carries the placement; the other keeps the kind preset's
        // default bearer, and the file never names it twice.
        ProviderEntry loaded = Assert.Single(store.Entries);
        Assert.Equal(
            CredentialPlacementTarget.Query,
            loaded.CredentialPlacements![ProviderApiFamily.Google].Target
        );
        Assert.Single(loaded.CredentialPlacements);
        Assert.Equal(CredentialPlacement.Bearer, loaded.PlacementFor(ProviderApiFamily.OpenAiChat));

        // The store writes the family-keyed form even for one family: a provider-wide object
        // would mean "every family" on read and would silently widen on reload.
        bool wideCreated = ProviderEntry.TryCreate(
            "wide",
            "anthropic",
            new Uri("https://claude.example.test/v1"),
            "auth:wide",
            null,
            null,
            null,
            null,
            new Dictionary<ProviderApiFamily, CredentialPlacement>
            {
                [ProviderApiFamily.AnthropicMessages] = CredentialPlacement.XApiKey,
            },
            out ProviderEntry? wide,
            out _
        );
        Assert.True(wideCreated);
        ProviderStore.Save(temp.File("wide.json"), [wide!]);
        using JsonDocument wideDocument = JsonDocument.Parse(
            File.ReadAllText(temp.File("wide.json"))
        );
        JsonElement widePlacement = wideDocument
            .RootElement.GetProperty("providers")[0]
            .GetProperty("credentialPlacement");
        JsonElement keyed = widePlacement.GetProperty("anthropic-messages");
        Assert.Equal("x-api-key", keyed.GetString());
    }

    private static ProviderEntry CreateEntry(
        string name,
        Uri baseUrl,
        string credentialReference,
        IReadOnlyDictionary<string, string>? headers,
        string region,
        string kind = "openai",
        IReadOnlyDictionary<ProviderApiFamily, string>? baseUrls = null,
        string? modelsUrl = null
    )
    {
        Dictionary<ProviderApiFamily, Uri>? familyUrls = null;
        if (baseUrls is { Count: > 0 } declared)
        {
            familyUrls = [];
            foreach ((ProviderApiFamily family, string urlValue) in declared)
            {
                familyUrls.Add(family, new Uri(urlValue));
            }
        }

        bool created = ProviderEntry.TryCreate(
            name,
            kind,
            baseUrl,
            credentialReference,
            headers,
            region,
            familyUrls,
            modelsUrl is null ? null : new Uri(modelsUrl),
            out ProviderEntry? entry,
            out string? error
        );

        Assert.True(created, error);
        return Assert.IsType<ProviderEntry>(entry);
    }

    private sealed class StatusHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}

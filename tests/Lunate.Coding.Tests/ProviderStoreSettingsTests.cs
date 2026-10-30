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
    public void A_literal_header_secret_cannot_be_saved()
    {
        using var temp = new TempDirectory();
        string path = temp.File("providers.json");
        const string secret = "fixture-only-header-secret";
        ProviderEntry invalid = new(
            "work-openai",
            "openai",
            new Uri("https://example.test/v1"),
            "auth:work",
            new Dictionary<string, string> { ["X-Api-Key"] = secret }
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ProviderStore.Save(path, [invalid])
        );

        Assert.Contains("headers.X-Api-Key", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
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

    private static ProviderEntry CreateEntry(
        string name,
        Uri baseUrl,
        string credentialReference,
        IReadOnlyDictionary<string, string> headers,
        string region
    )
    {
        bool created = ProviderEntry.TryCreate(
            name,
            "openai",
            baseUrl,
            credentialReference,
            headers,
            region,
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

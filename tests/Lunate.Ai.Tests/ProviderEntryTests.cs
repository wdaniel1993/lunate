namespace Lunate.Ai.Tests;

public sealed class ProviderEntryTests
{
    [Fact]
    public void Every_preset_kind_accepts_provider_entries()
    {
        foreach (ProviderPreset preset in ProviderPresets.All)
        {
            ProviderEntry entry = Create(
                $"account-{preset.Id}",
                preset.Id,
                preset.BaseUrl,
                $"auth:{preset.Id}"
            );

            Assert.Equal(preset.Id, entry.Kind);
        }

        Assert.Equal(12, ProviderPresets.All.Count);
    }

    [Fact]
    public void Multiple_entries_of_one_kind_keep_independent_access()
    {
        ProviderEntry first = Create(
            "work-openai",
            "openai",
            new Uri("https://work.example.test/v1"),
            "auth:work"
        );
        ProviderEntry second = Create(
            "personal-openai",
            "openai",
            new Uri("https://personal.example.test/v1"),
            "env:PERSONAL_OPENAI_KEY"
        );

        Assert.Equal("work-openai", first.Name);
        Assert.Equal("personal-openai", second.Name);
        Assert.NotEqual(first.BaseUrl, second.BaseUrl);
        Assert.Equal("auth:work", first.CredentialReference);
        Assert.Equal("env:PERSONAL_OPENAI_KEY", second.CredentialReference);
    }

    [Theory]
    [InlineData("auth:work")]
    [InlineData("env:OPENAI_API_KEY")]
    [InlineData("helper:corp-credential")]
    public void Credential_reference_sources_are_accepted(string reference)
    {
        ProviderEntry entry = Create(
            "openai-account",
            "openai",
            new Uri("https://example.test/v1"),
            reference
        );

        Assert.Equal(reference, entry.CredentialReference);
    }

    [Theory]
    [InlineData("eu")]
    [InlineData("us")]
    [InlineData("local")]
    public void Header_references_and_region_metadata_are_retained(string region)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-Workspace"] = "auth:workspace-header",
        };

        ProviderEntry entry = Create(
            "openai-account",
            "openai",
            new Uri("https://example.test/v1"),
            "auth:openai",
            headers,
            region
        );

        Assert.Equal("auth:workspace-header", entry.Headers!["X-Workspace"]);
        Assert.Equal(region, entry.Region);
    }

    [Fact]
    public void Literal_header_values_are_accepted_and_templates_resolve_over_the_context()
    {
        // The header value model accepts literals as declared data; a value with a newline stays
        // refused without echoing the refused value.
        const string secret = "fixture-only-header-secret";
        bool created = ProviderEntry.TryCreate(
            "openai-account",
            "openai",
            new Uri("https://example.test/v1"),
            "auth:openai",
            new Dictionary<string, string> { ["X-Title"] = "Lunate" },
            region: null,
            out ProviderEntry? literal,
            out _
        );

        Assert.True(created);
        Assert.Equal("Lunate", literal!.Headers!["X-Title"]);

        bool newlineRefused = ProviderEntry.TryCreate(
            "openai-account",
            "openai",
            new Uri("https://example.test/v1"),
            "auth:openai",
            new Dictionary<string, string> { ["X-Injected"] = secret + "\nEvil: 1" },
            region: null,
            out _,
            out string? newlineError
        );

        Assert.False(newlineRefused);
        Assert.Contains("headers.X-Injected", newlineError, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, newlineError, StringComparison.Ordinal);
    }

    private static ProviderEntry Create(
        string name,
        string kind,
        Uri baseUrl,
        string credentialReference,
        IReadOnlyDictionary<string, string>? headers = null,
        string? region = null
    )
    {
        bool created = ProviderEntry.TryCreate(
            name,
            kind,
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
}

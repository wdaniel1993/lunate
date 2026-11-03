using System.Text.Json;

namespace Lunate.Ai.Tests;

/// <summary>
/// The header value model at load: names, values and placeholders are validated where providers
/// and entries are parsed, a template never carries the credential (the three refused names), and
/// the declared placements survive parsing with their compact and object file forms.
/// </summary>
public sealed class ProviderHeaderModelTests
{
    [Theory]
    [InlineData("auth:work")]
    [InlineData("env:WORK_HEADER_TOKEN")]
    [InlineData("helper:corp-credential")]
    public void Credential_reference_values_stay_accepted(string reference)
    {
        ProviderEntry entry = CreateEntry(
            new Dictionary<string, string> { ["X-Workspace"] = reference }
        );

        Assert.Equal(reference, entry.Headers!["X-Workspace"]);
    }

    [Theory]
    [InlineData("{credential}")]
    [InlineData("{apiKey}")]
    [InlineData("{key}")]
    public void A_template_cannot_reference_the_credential(string placeholder)
    {
        bool created = ProviderEntry.TryCreate(
            "openai-account",
            "openai",
            new Uri("https://example.test/v1"),
            "auth:openai",
            new Dictionary<string, string> { ["X-Session"] = placeholder },
            region: null,
            out _,
            out string? error
        );

        Assert.False(created);
        Assert.Contains("headers.X-Session", error, StringComparison.Ordinal);
        Assert.Contains(placeholder[1..^1], error, StringComparison.Ordinal);
        Assert.Contains("cannot carry it", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_placeholder_is_refused_naming_it()
    {
        bool created = ProviderEntry.TryCreate(
            "openai-account",
            "openai",
            new Uri("https://example.test/v1"),
            "auth:openai",
            new Dictionary<string, string> { ["X-Env"] = "{environment.HOSTNAME}" },
            region: null,
            out _,
            out string? error
        );

        Assert.False(created);
        Assert.Contains("{environment.HOSTNAME}", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("x opencode", "value")]
    [InlineData("x:colon", "value")]
    [InlineData("", "value")]
    [InlineData("fine-name", "bad\tvalue")]
    [InlineData("fine-name", "")]
    public void Invalid_header_names_or_values_are_refused(string name, string value)
    {
        bool created = ProviderEntry.TryCreate(
            "openai-account",
            "openai",
            new Uri("https://example.test/v1"),
            "auth:openai",
            new Dictionary<string, string> { [name] = value },
            region: null,
            out _,
            out _
        );

        Assert.False(created);
    }

    [Fact]
    public void A_line_break_in_a_value_is_refused_without_echoing_it()
    {
        bool created = ProviderEntry.TryCreate(
            "openai-account",
            "openai",
            new Uri("https://example.test/v1"),
            "auth:openai",
            new Dictionary<string, string> { ["X-Broken"] = "two\nlines" },
            region: null,
            out _,
            out string? error
        );

        Assert.False(created);
        Assert.NotNull(error);
        Assert.Contains("X-Broken", error, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', error);
    }

    [Theory]
    [InlineData("{session.id}")]
    [InlineData("{run.id}")]
    [InlineData("{lunate.version}")]
    public void The_three_known_placeholders_are_accepted(string placeholder)
    {
        ProviderEntry entry = CreateEntry(
            new Dictionary<string, string> { ["X-Session"] = placeholder }
        );

        Assert.Equal(placeholder, entry.Headers!["X-Session"]);
    }

    [Theory]
    [InlineData("bearer")]
    [InlineData("x-api-key")]
    [InlineData("query:key")]
    [InlineData("header:api-key")]
    public void Compact_placement_forms_parse(string compact)
    {
        bool parsed = CredentialPlacement.TryParseCompact(
            compact,
            out CredentialPlacement? placement,
            out string? error
        );

        Assert.True(parsed, error);
        Assert.NotNull(placement);
        Assert.Equal(compact, placement!.ToCompact());
    }

    [Theory]
    [InlineData("query key")]
    [InlineData("azure")]
    [InlineData("header:")]
    public void Unknown_compact_forms_are_refused_by_name(string compact)
    {
        bool parsed = CredentialPlacement.TryParseCompact(compact, out _, out string? error);

        Assert.False(parsed);
        Assert.Contains(compact, error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_object_placement_carries_the_name_and_the_scheme()
    {
        using var document = JsonDocument.Parse(
            """{ "in": "header", "name": "api-key", "scheme": "Shared" }"""
        );
        JsonElement element = document.RootElement.Clone();

        bool parsed = CredentialPlacementFile.TryParseProvider(
            element,
            out Dictionary<ProviderApiFamily, CredentialPlacement>? placements,
            out string? error
        );

        Assert.True(parsed, error);
        CredentialPlacement placement = placements![ProviderApiFamily.OpenAiChat];
        Assert.Equal("api-key", placement.Name);
        Assert.Equal("Shared ", placement.Scheme); // One trailing space, as the header value needs.
    }

    [Fact]
    public void A_family_keyed_placement_applies_per_family()
    {
        using var document = JsonDocument.Parse(
            """{ "google": { "in": "query", "name": "key" }, "openai-chat": "x-api-key" }"""
        );
        JsonElement element = document.RootElement.Clone();

        bool parsed = CredentialPlacementFile.TryParseProvider(
            element,
            out Dictionary<ProviderApiFamily, CredentialPlacement>? placements,
            out string? error
        );

        Assert.True(parsed, error);
        Assert.Equal(CredentialPlacementTarget.Query, placements![ProviderApiFamily.Google].Target);
        Assert.Equal("x-api-key", placements[ProviderApiFamily.OpenAiChat].Name);
    }

    [Fact]
    public void An_unknown_family_key_is_refused_with_the_supported_set()
    {
        using var document = JsonDocument.Parse("""{ "chat-completions": "bearer" }""");
        JsonElement element = document.RootElement.Clone();

        bool parsed = CredentialPlacementFile.TryParseProvider(element, out _, out string? error);

        Assert.False(parsed);
        Assert.Contains("'chat-completions'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_placement_wins_over_the_preset_and_defaults_stay_bearer()
    {
        ProviderPreset preset = ProviderPresets.Find("anthropic")!;
        Assert.Equal(
            CredentialPlacement.XApiKey,
            preset.PlacementFor(ProviderApiFamily.AnthropicMessages)
        );

        ProviderPreset google = ProviderPresets.Find("google")!;
        Assert.Equal(
            CredentialPlacementTarget.Query,
            google.PlacementFor(ProviderApiFamily.Google).Target
        );
        Assert.Equal(CredentialPlacement.Bearer, google.PlacementFor(ProviderApiFamily.OpenAiChat));

        ProviderPreset openai = ProviderPresets.Find("openai")!;
        Assert.Equal(CredentialPlacement.Bearer, openai.PlacementFor(ProviderApiFamily.OpenAiChat));
    }

    [Fact]
    public void An_entry_without_a_placement_keeps_the_kind_preset_placement()
    {
        // anthropic kind without an entry-level placement carries the preset's x-api-key.
        ProviderEntry entry = new(
            "claude",
            "anthropic",
            new Uri("https://claude-proxy.example.test/v1"),
            "auth:claude"
        );

        Assert.Equal(
            CredentialPlacement.XApiKey,
            entry.PlacementFor(ProviderApiFamily.AnthropicMessages)
        );
    }

    [Fact]
    public void The_resolved_profile_carries_the_entry_headers_and_the_family_placement()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(
            temp,
            """{ "id": "go/beam", "provider": "opencode-go", "contextWindow": 8192, "supportsTools": true }"""
        );
        ProviderEntry provider = new(
            "go",
            "opencode-go",
            new Uri("https://go.example.test/openai/v1"),
            "auth:go",
            Headers: new Dictionary<string, string> { ["x-tenant"] = "{session.id}" },
            BaseUrls: new Dictionary<ProviderApiFamily, Uri>
            {
                [ProviderApiFamily.OpenAiChat] = new("https://go.example.test/openai/v1"),
                [ProviderApiFamily.Google] = new("https://go.example.test/google/v1beta"),
            },
            CredentialPlacements: new Dictionary<ProviderApiFamily, CredentialPlacement>
            {
                [ProviderApiFamily.OpenAiChat] = CredentialPlacement.XApiKey,
            }
        );
        var profile = new ModelProfile("chat", "go", "go/beam", ApiFamily: "openai-chat");

        ResolvedModelProfile resolved = ModelProfileResolver.Resolve(profile, [provider], catalog);

        // The raw templates ride on the model (never resolved secrets) and the compact
        // placement names the resolved family's credential form.
        Assert.Equal("{session.id}", resolved.Model.ProviderHeaders!["x-tenant"]);
        Assert.Equal("x-api-key", resolved.Model.CredentialPlacement);
    }

    [Fact]
    public void An_entry_without_a_declared_placement_keeps_the_preset_one()
    {
        using var temp = new TempDirectory();
        ModelCatalog catalog = Catalog(
            temp,
            """{ "id": "say/claude", "provider": "anthropic", "contextWindow": 8192, "supportsTools": true }"""
        );
        ProviderEntry provider = new(
            "say",
            "anthropic",
            new Uri("https://claude.example.test/v1"),
            "auth:say"
        );
        var profile = new ModelProfile("chat", "say", "say/claude");

        ResolvedModelProfile resolved = ModelProfileResolver.Resolve(profile, [provider], catalog);

        // The anthropic preset's placement wins where the entry declares nothing.
        Assert.Null(resolved.Model.ProviderHeaders);
        Assert.Equal("x-api-key", resolved.Model.CredentialPlacement);
    }

    /// <summary>The one-model user catalog the profile tests resolve against.</summary>
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

    private static ProviderEntry CreateEntry(IReadOnlyDictionary<string, string> headers)
    {
        bool created = ProviderEntry.TryCreate(
            "openai-account",
            "openai",
            new Uri("https://example.test/v1"),
            "auth:openai",
            headers,
            region: null,
            out ProviderEntry? entry,
            out string? error
        );

        Assert.True(created, error);
        return entry!;
    }
}

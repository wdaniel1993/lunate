using Lunate.Ai;

namespace Lunate.Coding.Tests;

/// <summary>
/// The welcome header's first-run check (design D4): exactly one actionable fix when no credential
/// resolves, and silence when the model needs none.
/// </summary>
public sealed class ProviderKeyProbeTests
{
    private static readonly ModelInfo OpenAiModel = new(
        "gpt-4o-mini",
        "openai",
        null,
        128_000,
        true
    );

    [Fact]
    public void A_resolving_environment_key_needs_no_fix() =>
        Assert.Null(
            ProviderKeyProbe.FixFor(
                OpenAiModel,
                EmptyStore(),
                name => name == "OPENAI_API_KEY" ? "sk-test" : null
            )
        );

    [Fact]
    public void A_stored_provider_key_needs_no_fix() =>
        Assert.Null(ProviderKeyProbe.FixFor(OpenAiModel, StoreWith("openai"), _ => null));

    [Fact]
    public void A_missing_key_names_auth_as_the_next_step() =>
        Assert.Equal(
            "run /auth to configure a provider",
            ProviderKeyProbe.FixFor(OpenAiModel, EmptyStore(), _ => null)
        );

    [Fact]
    public void An_empty_environment_key_names_the_variable() =>
        Assert.Equal(
            "set OPENAI_API_KEY to a valid key",
            ProviderKeyProbe.FixFor(
                OpenAiModel,
                EmptyStore(),
                name => name == "OPENAI_API_KEY" ? " " : null
            )
        );

    [Fact]
    public void A_declared_endpoint_needs_no_key() =>
        Assert.Null(
            ProviderKeyProbe.FixFor(
                OpenAiModel with
                {
                    Endpoint = new Uri("http://localhost:11434/v1"),
                },
                EmptyStore(),
                _ => null
            )
        );

    [Fact]
    public void An_endpoint_equal_to_the_presets_base_url_probes_the_preset_key() =>
        Assert.Equal(
            "run /auth to configure a provider",
            ProviderKeyProbe.FixFor(
                GroqModel() with
                {
                    Endpoint = ProviderPresets.Find("groq")!.BaseUrl,
                },
                EmptyStore(),
                _ => null
            )
        );

    [Fact]
    public void A_missing_named_reference_on_a_declared_endpoint_names_that_key() =>
        Assert.Equal(
            "add the 'work' key to ~/.lunate/auth.json",
            ProviderKeyProbe.FixFor(
                OpenAiModel with
                {
                    Endpoint = new Uri("http://localhost:11434/v1"),
                    AuthRef = "work",
                },
                EmptyStore(),
                _ => null
            )
        );

    [Fact]
    public void A_missing_named_reference_names_that_key() =>
        Assert.Equal(
            "add the 'work' key to ~/.lunate/auth.json",
            ProviderKeyProbe.FixFor(OpenAiModel with { AuthRef = "work" }, EmptyStore(), _ => null)
        );

    [Fact]
    public void A_resolving_named_reference_needs_no_fix() =>
        Assert.Null(
            ProviderKeyProbe.FixFor(
                OpenAiModel with
                {
                    AuthRef = "work",
                },
                StoreWith("work"),
                _ => null
            )
        );

    [Fact]
    public void A_resolving_preset_environment_key_needs_no_fix() =>
        Assert.Null(
            ProviderKeyProbe.FixFor(
                GroqModel(),
                EmptyStore(),
                name => name == "GROQ_API_KEY" ? "gsk-test" : null
            )
        );

    [Fact]
    public void A_stored_preset_key_needs_no_fix() =>
        Assert.Null(ProviderKeyProbe.FixFor(GroqModel(), StoreWith("groq"), _ => null));

    [Fact]
    public void A_missing_preset_key_names_auth_as_the_next_step() =>
        Assert.Equal(
            "run /auth to configure a provider",
            ProviderKeyProbe.FixFor(GroqModel(), EmptyStore(), _ => null)
        );

    [Fact]
    public void An_empty_preset_environment_key_names_the_preset_variable() =>
        Assert.Equal(
            "set GROQ_API_KEY to a valid key",
            ProviderKeyProbe.FixFor(
                GroqModel(),
                EmptyStore(),
                name => name == "GROQ_API_KEY" ? " " : null
            )
        );

    [Fact]
    public void A_whitespace_preset_environment_key_falls_back_to_the_stored_key() =>
        Assert.Null(
            ProviderKeyProbe.FixFor(
                GroqModel(),
                StoreWith("groq"),
                name => name == "GROQ_API_KEY" ? " " : null
            )
        );

    [Fact]
    public void A_whitespace_stored_preset_key_names_auth_as_the_next_step() =>
        Assert.Equal(
            "run /auth to configure a provider",
            ProviderKeyProbe.FixFor(GroqModel(), StoreWithValue("groq", "   "), _ => null)
        );

    [Fact]
    public void A_whitespace_google_alias_falls_back_to_the_stored_key() =>
        Assert.Null(
            ProviderKeyProbe.FixFor(
                GoogleModel(),
                StoreWith("google"),
                name => name == "GEMINI_API_KEY" ? " " : null
            )
        );

    [Fact]
    public void A_whitespace_named_reference_names_that_key() =>
        Assert.Equal(
            "add the 'work' key to ~/.lunate/auth.json",
            ProviderKeyProbe.FixFor(
                OpenAiModel with
                {
                    AuthRef = "work",
                },
                StoreWithValue("work", "   "),
                _ => null
            )
        );

    [Fact]
    public void The_google_alias_resolves_the_preset_key() =>
        Assert.Null(
            ProviderKeyProbe.FixFor(
                GoogleModel(),
                EmptyStore(),
                name => name == "GEMINI_API_KEY" ? "gemini-test" : null
            )
        );

    [Fact]
    public void A_missing_google_key_names_auth_as_the_next_step() =>
        Assert.Equal(
            "run /auth to configure a provider",
            ProviderKeyProbe.FixFor(GoogleModel(), EmptyStore(), _ => null)
        );

    [Fact]
    public void An_unknown_provider_gets_no_credential_advice() =>
        Assert.Null(
            ProviderKeyProbe.FixFor(OpenAiModel with { Provider = "acme" }, EmptyStore(), _ => null)
        );

    private static ModelInfo GroqModel() => new("groq/allam-2-7b", "groq", null, 128_000, true);

    private static ModelInfo GoogleModel() =>
        new("google/deep-research-max-preview-04-2026", "google", null, 131_072, true);

    private static AuthStore EmptyStore() =>
        AuthStore.Load(
            Path.Combine(Path.GetTempPath(), "lunate-tests-missing-auth.json"),
            _ => null
        );

    private static AuthStore StoreWith(string name)
    {
        using var temp = new TempDirectory();
        string path = temp.File("auth.json");
        AuthStore store = AuthStore.Load(path, _ => null);
        store.Set(name, "sk-test");
        store.Save();
        return AuthStore.Load(path, _ => null);
    }

    private static AuthStore StoreWithValue(string name, string value)
    {
        using var temp = new TempDirectory();
        string path = temp.File("auth.json");
        File.WriteAllText(
            path,
            $$"""{ "schemaVersion": 1, "keys": { "{{name}}": "{{value}}" } }"""
        );
        return AuthStore.Load(path, _ => null);
    }
}

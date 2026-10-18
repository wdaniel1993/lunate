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
    public void A_missing_key_names_the_auth_store_entry() =>
        Assert.Equal(
            "add the 'openai' key to ~/.lunate/auth.json",
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
    public void An_unknown_provider_gets_no_credential_advice() =>
        Assert.Null(
            ProviderKeyProbe.FixFor(OpenAiModel with { Provider = "acme" }, EmptyStore(), _ => null)
        );

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
}

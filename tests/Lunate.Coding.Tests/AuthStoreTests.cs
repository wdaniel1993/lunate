using Lunate.Ai;

namespace Lunate.Coding.Tests;

public sealed class AuthStoreTests
{
    private const string OpenAiVariable = "OPENAI_API_KEY";
    private const string AnthropicVariable = "ANTHROPIC_API_KEY";

    [Fact]
    public void Missing_file_yields_an_empty_store()
    {
        using var temp = new TempDirectory();

        AuthStore store = AuthStore.Load(temp.File("missing.json"), _ => null);

        Assert.Empty(store.Keys);
        Assert.Null(store.TryGet("work"));
    }

    [Fact]
    public void Load_reads_named_keys()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("auth.json"),
            """
            {
              "schemaVersion": 1,
              "keys": { "work": "work-secret", "openai": "file-openai" }
            }
            """
        );

        AuthStore store = AuthStore.Load(temp.File("auth.json"), _ => null);

        Assert.Equal("work-secret", store.TryGet("work"));
        Assert.Equal("file-openai", store.TryGet("openai"));
    }

    [Fact]
    public void Environment_beats_the_file_for_provider_defaults()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("auth.json"),
            """
            { "schemaVersion": 1, "keys": { "openai": "file-openai", "anthropic": "file-anthropic" } }
            """
        );

        AuthStore store = AuthStore.Load(
            temp.File("auth.json"),
            name =>
                name switch
                {
                    OpenAiVariable => "env-openai",
                    AnthropicVariable => "env-anthropic",
                    _ => null,
                }
        );

        Assert.Equal("env-openai", store.TryGet("openai"));
        Assert.Equal("env-anthropic", store.TryGet("anthropic"));
    }

    [Theory]
    [InlineData("openai", "OPENAI_API_KEY")]
    [InlineData("anthropic", "ANTHROPIC_API_KEY")]
    [InlineData("openrouter", "OPENROUTER_API_KEY")]
    [InlineData("deepseek", "DEEPSEEK_API_KEY")]
    [InlineData("moonshotai", "MOONSHOT_API_KEY")]
    [InlineData("zai", "ZHIPU_API_KEY")]
    [InlineData("opencode", "OPENCODE_API_KEY")]
    [InlineData("opencode-go", "OPENCODE_API_KEY")]
    [InlineData("groq", "GROQ_API_KEY")]
    [InlineData("togetherai", "TOGETHER_API_KEY")]
    [InlineData("xai", "XAI_API_KEY")]
    [InlineData("google", "GOOGLE_API_KEY")]
    public void Every_preset_environment_variable_overrides_its_entry(
        string provider,
        string variable
    )
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("auth.json"),
            $$"""
            { "schemaVersion": 1, "keys": { "{{provider}}": "file-value" } }
            """
        );

        AuthStore store = AuthStore.Load(
            temp.File("auth.json"),
            name => name == variable ? "env-value" : null
        );

        Assert.Equal("env-value", store.TryGet(provider));
    }

    [Fact]
    public void The_google_alias_overrides_the_file_entry()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("auth.json"),
            """{ "schemaVersion": 1, "keys": { "google": "file-value" } }"""
        );

        AuthStore store = AuthStore.Load(
            temp.File("auth.json"),
            name => name == "GEMINI_API_KEY" ? "alias-value" : null
        );

        Assert.Equal("alias-value", store.TryGet("google"));
    }

    [Fact]
    public void The_key_variable_comes_from_the_preset_table()
    {
        Assert.Equal("GROQ_API_KEY", ProviderPresets.Find("groq")?.KeyVariable);
        Assert.Equal("GEMINI_API_KEY", ProviderPresets.Find("google")?.KeyVariableAlias);
    }

    [Fact]
    public void Custom_named_keys_have_no_environment_override()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("auth.json"),
            """{ "schemaVersion": 1, "keys": { "work": "work-secret" } }"""
        );

        AuthStore store = AuthStore.Load(temp.File("auth.json"), _ => "unrelated");

        Assert.Equal("work-secret", store.TryGet("work"));
    }

    [Fact]
    public void Save_round_trips_the_keys()
    {
        using var temp = new TempDirectory();
        AuthStore store = AuthStore.Load(temp.File("auth.json"), _ => null);
        store.Set("work", "work-secret");
        store.Set("openai", "openai-secret");

        store.Save();
        AuthStore reloaded = AuthStore.Load(temp.File("auth.json"), _ => null);

        Assert.Equal("work-secret", reloaded.TryGet("work"));
        Assert.Equal("openai-secret", reloaded.TryGet("openai"));
    }

    [Fact]
    public void Remove_deletes_a_stored_key_and_saves_the_remaining_ones()
    {
        using var temp = new TempDirectory();
        AuthStore store = AuthStore.Load(temp.File("auth.json"), _ => null);
        store.Set("groq", "groq-secret");
        store.Set("work", "work-secret");

        Assert.True(store.Remove("groq"));
        Assert.False(store.Remove("groq"));
        store.Save();

        AuthStore reloaded = AuthStore.Load(temp.File("auth.json"), _ => null);
        Assert.Null(reloaded.TryGet("groq"));
        Assert.Equal("work-secret", reloaded.TryGet("work"));
    }

    [Fact]
    public void Save_replaces_the_file_without_leaving_a_temp_file()
    {
        using var temp = new TempDirectory();
        AuthStore store = AuthStore.Load(temp.File("auth.json"), _ => null);
        store.Set("groq", "groq-secret");

        store.Save();
        store.Set("work", "work-secret");
        store.Save();

        Assert.Empty(Directory.GetFiles(temp.Root, "*.tmp"));
        AuthStore reloaded = AuthStore.Load(temp.File("auth.json"), _ => null);
        Assert.Equal("groq-secret", reloaded.TryGet("groq"));
        Assert.Equal("work-secret", reloaded.TryGet("work"));
    }

    [Fact]
    public void Saved_auth_files_are_owner_only_on_posix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        AuthStore store = AuthStore.Load(temp.File("auth.json"), _ => null);
        store.Set("work", "work-secret");

        store.Save();

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(temp.File("auth.json"))
        );
    }

    [Fact]
    public void Require_names_the_missing_key_and_never_a_stored_value()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("auth.json"),
            """{ "schemaVersion": 1, "keys": { "openai": "super-secret-value" } }"""
        );
        AuthStore store = AuthStore.Load(temp.File("auth.json"), _ => null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            store.Require("missing")
        );

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
        Assert.Contains("auth.json", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_files_report_every_problem_without_values()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("auth.json"),
            """
            {
              "schemaVersion": 2,
              "unknown": true,
              "keys": { "work": { "secret": "leaked-value" } }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            AuthStore.Load(temp.File("auth.json"), _ => null)
        );

        Assert.Contains("schemaVersion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unknown", exception.Message, StringComparison.Ordinal);
        Assert.Contains("work", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("leaked-value", exception.Message, StringComparison.Ordinal);
    }
}

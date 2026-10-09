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

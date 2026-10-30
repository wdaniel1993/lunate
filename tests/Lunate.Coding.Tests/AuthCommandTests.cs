using Lunate.Ai;

namespace Lunate.Coding.Tests;

public sealed class AuthCommandTests
{
    private const string StoredOpenAi = "sk-stored-openai-secret";
    private const string GroqSecret = "gsk-groq-secret-value";
    private const string OpenAiEnvironment = "sk-openai-environment-secret";

    [Fact]
    public void List_reports_stored_and_environment_sources_without_values()
    {
        using var temp = new TempDirectory();
        StoreKey(temp, "openai", StoredOpenAi);
        var environment = new Dictionary<string, string?> { ["GROQ_API_KEY"] = GroqSecret };

        (int exit, string output, string errors) = Run(
            ["list"],
            Seams(temp, environment) with
            {
                ProviderProbeHandler = new StatusHandler(),
            }
        );

        Assert.Equal(0, exit);
        Assert.Contains("openai", output, StringComparison.Ordinal);
        Assert.Contains("stored", output, StringComparison.Ordinal);
        Assert.Contains("groq", output, StringComparison.Ordinal);
        Assert.Contains("environment (GROQ_API_KEY)", output, StringComparison.Ordinal);
        Assert.DoesNotContain(StoredOpenAi, output + errors, StringComparison.Ordinal);
        Assert.DoesNotContain(GroqSecret, output + errors, StringComparison.Ordinal);
    }

    [Fact]
    public void List_with_nothing_stored_lists_every_preset_and_exits_zero()
    {
        using var temp = new TempDirectory();

        (int exit, string output, _) = Run(["list"], Seams(temp));

        Assert.Equal(0, exit);
        foreach (ProviderPreset preset in ProviderPresets.All)
        {
            Assert.Contains(preset.Id, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Login_with_key_stdin_stores_the_key_without_echoing_it()
    {
        using var temp = new TempDirectory();
        var prompt = new ScriptedSecretPrompt();
        var placeholder = new StringReader(GroqSecret + "\n");
        AuthCommandSeams seams = Seams(temp, stdinIsTerminal: false, prompt: prompt) with
        {
            Input = placeholder,
        };

        (int exit, string output, string errors) = Run(["login", "groq", "--key-stdin"], seams);

        Assert.Equal(0, exit);
        Assert.Equal(GroqSecret, AuthStore.Load(seams.AuthPath, _ => null).Keys["groq"]);
        Assert.DoesNotContain(GroqSecret, output + errors, StringComparison.Ordinal);
        Assert.All(
            prompt.Prompts,
            label => Assert.DoesNotContain(GroqSecret, label, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Login_on_a_terminal_uses_the_masked_prompt()
    {
        using var temp = new TempDirectory();
        var prompt = new ScriptedSecretPrompt(GroqSecret);
        AuthCommandSeams seams = Seams(temp, stdinIsTerminal: true, prompt: prompt);

        (int exit, string output, string errors) = Run(["login", "groq"], seams);

        Assert.Equal(0, exit);
        Assert.Equal(["API key for groq"], prompt.Prompts);
        Assert.Equal(GroqSecret, AuthStore.Load(seams.AuthPath, _ => null).Keys["groq"]);
        Assert.DoesNotContain(GroqSecret, output + errors, StringComparison.Ordinal);
    }

    [Fact]
    public void Login_without_a_provider_on_a_pipe_lists_presets_on_stderr_and_exits_two()
    {
        using var temp = new TempDirectory();
        var prompt = new ScriptedSecretPrompt(GroqSecret);

        (int exit, string output, string errors) = Run(
            ["login"],
            Seams(temp, stdinIsTerminal: false, prompt: prompt)
        );

        Assert.Equal(2, exit);
        Assert.Empty(output);
        foreach (ProviderPreset preset in ProviderPresets.All)
        {
            Assert.Contains(preset.Id, errors, StringComparison.Ordinal);
        }

        Assert.Empty(prompt.Prompts);
    }

    [Fact]
    public void Login_without_a_provider_on_a_terminal_lists_presets_and_exits_zero()
    {
        using var temp = new TempDirectory();
        var prompt = new ScriptedSecretPrompt(GroqSecret);

        (int exit, string output, _) = Run(
            ["login"],
            Seams(temp, stdinIsTerminal: true, prompt: prompt)
        );

        Assert.Equal(0, exit);
        foreach (ProviderPreset preset in ProviderPresets.All)
        {
            Assert.Contains(preset.Id, output, StringComparison.Ordinal);
        }

        Assert.Empty(prompt.Prompts);
    }

    [Fact]
    public void Login_with_piped_stdin_and_no_key_stdin_flag_exits_two()
    {
        using var temp = new TempDirectory();
        var prompt = new ScriptedSecretPrompt(GroqSecret);

        (int exit, _, string errors) = Run(
            ["login", "groq"],
            Seams(temp, stdinIsTerminal: false, prompt: prompt)
        );

        Assert.Equal(2, exit);
        Assert.Contains("--key-stdin", errors, StringComparison.Ordinal);
        Assert.Empty(prompt.Prompts);
    }

    [Fact]
    public void Login_with_an_unknown_provider_lists_ids_without_echoing_the_token()
    {
        using var temp = new TempDirectory();
        const string token = "gsk-this-is-a-pasted-key";

        (int exit, _, string errors) = Run(["login", token], Seams(temp, stdinIsTerminal: true));

        Assert.Equal(2, exit);
        Assert.DoesNotContain(token, errors, StringComparison.Ordinal);
        foreach (ProviderPreset preset in ProviderPresets.All)
        {
            Assert.Contains(preset.Id, errors, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Logout_removes_the_entry_and_names_a_still_set_variable()
    {
        using var temp = new TempDirectory();
        StoreKey(temp, "groq", GroqSecret);
        var environment = new Dictionary<string, string?> { ["GROQ_API_KEY"] = GroqSecret };

        (int exit, string output, string errors) = Run(
            ["logout", "groq"],
            Seams(temp, environment)
        );

        Assert.Equal(0, exit);
        Assert.False(AuthStore.Load(temp.File("auth.json"), _ => null).Keys.ContainsKey("groq"));
        Assert.Contains("GROQ_API_KEY", output, StringComparison.Ordinal);
        Assert.DoesNotContain(GroqSecret, output + errors, StringComparison.Ordinal);
    }

    [Fact]
    public void Logout_with_nothing_stored_exits_one_and_names_the_variable()
    {
        using var temp = new TempDirectory();
        var environment = new Dictionary<string, string?> { ["GROQ_API_KEY"] = GroqSecret };

        (int exit, _, string errors) = Run(["logout", "groq"], Seams(temp, environment));

        Assert.Equal(1, exit);
        Assert.Contains("'groq'", errors, StringComparison.Ordinal);
        Assert.Contains("GROQ_API_KEY", errors, StringComparison.Ordinal);
        Assert.DoesNotContain(GroqSecret, errors, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_reports_the_default_provider_and_a_stored_credential()
    {
        using var temp = new TempDirectory();
        WriteSettings(temp, "gpt-5");
        StoreKey(temp, "openai", StoredOpenAi);

        (int exit, string output, string errors) = Run(["status"], Seams(temp));

        Assert.Equal(0, exit);
        Assert.Contains("default provider: openai", output, StringComparison.Ordinal);
        Assert.Contains("credential: stored", output, StringComparison.Ordinal);
        Assert.DoesNotContain(StoredOpenAi, output + errors, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_without_a_credential_exits_one()
    {
        using var temp = new TempDirectory();
        WriteSettings(temp, "gpt-5");

        (int exit, string output, _) = Run(["status"], Seams(temp));

        Assert.Equal(1, exit);
        Assert.Contains("credential: missing", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_reports_an_environment_credential()
    {
        using var temp = new TempDirectory();
        WriteSettings(temp, "gpt-5");
        var environment = new Dictionary<string, string?>
        {
            ["OPENAI_API_KEY"] = OpenAiEnvironment,
        };

        (int exit, string output, _) = Run(["status"], Seams(temp, environment));

        Assert.Equal(0, exit);
        Assert.Contains(
            "credential: environment (OPENAI_API_KEY)",
            output,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(OpenAiEnvironment, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_routes_auth_to_the_command()
    {
        using var temp = new TempDirectory();
        StoreKey(temp, "openai", StoredOpenAi);
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exit = Cli.Run(
            ["auth", "list"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            authSeams: Seams(temp) with
            {
                ProviderProbeHandler = new StatusHandler(),
            }
        );

        Assert.Equal(0, exit);
        Assert.Contains("stored", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(errors.ToString());
    }

    private static (int Exit, string Output, string Errors) Run(
        string[] args,
        AuthCommandSeams seams
    )
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        int exit = AuthCommand.Run(args, output, errors, seams);
        return (exit, output.ToString(), errors.ToString());
    }

    private static AuthCommandSeams Seams(
        TempDirectory temp,
        Dictionary<string, string?>? environment = null,
        ISecretPrompt? prompt = null,
        bool stdinIsTerminal = false
    ) =>
        new()
        {
            AuthPath = temp.File("auth.json"),
            SettingsPath = temp.File("settings.json"),
            ModelsPath = temp.File("models.json"),
            Environment = name => environment?.GetValueOrDefault(name),
            SecretPrompt = prompt,
            StdinIsTerminal = stdinIsTerminal,
        };

    private static void WriteSettings(TempDirectory temp, string model) =>
        File.WriteAllText(
            temp.File("settings.json"),
            $$"""{"schemaVersion": 2, "model": "{{model}}"}"""
        );

    private static void StoreKey(TempDirectory temp, string name, string secret)
    {
        AuthStore store = AuthStore.Load(temp.File("auth.json"), _ => null);
        store.Set(name, secret);
        store.Save();
    }

    private sealed class ScriptedSecretPrompt(params string?[] values) : ISecretPrompt
    {
        private readonly Queue<string?> _values = new(values);

        public List<string> Prompts { get; } = [];

        public Task<string?> ReadAsync(string prompt, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            return Task.FromResult(_values.Count > 0 ? _values.Dequeue() : null);
        }
    }

    private sealed class StatusHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}

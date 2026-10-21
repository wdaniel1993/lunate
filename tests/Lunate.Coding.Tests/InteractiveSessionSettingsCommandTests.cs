using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

/// <summary>
/// The provider and favourites commands: secrets never echo, auth writes stay owner-only, and
/// favourites persist through the schema-2 settings writer without touching unrelated keys.
/// </summary>
public sealed class InteractiveSessionSettingsCommandTests
{
    [Fact]
    public async Task Login_stores_the_key_and_never_echoes_it()
    {
        const string secret = "groq-secret-value";
        var prompt = new ScriptedSecretPrompt(secret);
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            secretPrompt: prompt
        );
        string authPath = host.Temp.File("auth.json");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/login groq");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return File.Exists(authPath)
                && host.LastFrame.Contains("logged in: groq", StringComparison.Ordinal);
        });

        Assert.Equal(["API key for groq"], prompt.Prompts);
        AuthStore store = AuthStore.Load(authPath, _ => null);
        Assert.Equal(secret, store.TryGet("groq"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(authPath)
            );
        }

        Assert.DoesNotContain(secret, host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, host.ScrollbackText, StringComparison.Ordinal);
        foreach (
            string file in Directory.EnumerateFiles(
                host.Temp.Root,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            if (!string.Equals(file, authPath, StringComparison.Ordinal))
            {
                Assert.DoesNotContain(secret, File.ReadAllText(file), StringComparison.Ordinal);
            }
        }

        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Login_cancels_without_saving_on_escape_or_an_empty_key(string? value)
    {
        var prompt = new ScriptedSecretPrompt(value);
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            secretPrompt: prompt
        );
        string authPath = host.Temp.File("auth.json");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/login groq");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "login cancelled");

        Assert.Single(prompt.Prompts);
        Assert.False(File.Exists(authPath));
        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Login_with_an_inline_key_is_refused_without_echoing_or_recording_it()
    {
        const string secret = "sk-inline-secret";
        var prompt = new ScriptedSecretPrompt("never-read");
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            secretPrompt: prompt
        );
        string authPath = host.Temp.File("auth.json");
        string historyPath = host.Temp.File("history");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await host.SendLineAsync("/login groq " + secret);
        await WaitForFrameAsync(host, "takes no key argument");

        Assert.Contains("masked prompt", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, host.ScrollbackText, StringComparison.Ordinal);
        Assert.Empty(prompt.Prompts);
        Assert.False(File.Exists(authPath));
        Assert.False(File.Exists(historyPath));
        AssertNoFileContains(host, secret);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Login_with_an_unknown_provider_and_an_inline_key_echoes_nothing()
    {
        const string secret = "sk-inline-secret";
        var prompt = new ScriptedSecretPrompt("never-read");
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            secretPrompt: prompt
        );
        string historyPath = host.Temp.File("history");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await host.SendLineAsync("/login not-a-provider " + secret);
        await WaitForFrameAsync(host, "unknown provider for /login");

        Assert.DoesNotContain("not-a-provider", host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, host.ScrollbackText, StringComparison.Ordinal);
        Assert.Empty(prompt.Prompts);
        Assert.False(File.Exists(host.Temp.File("auth.json")));
        Assert.False(File.Exists(historyPath));
        AssertNoFileContains(host, secret);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Login_lines_stay_out_of_history_while_other_commands_are_still_recorded()
    {
        var prompt = new ScriptedSecretPrompt("groq-secret-value");
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            secretPrompt: prompt
        );
        string historyPath = host.Temp.File("history");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/help");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => File.Exists(historyPath));

        host.Console.SendText("/login groq");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "logged in: groq");

        Assert.Equal(["/help"], new InputHistory(historyPath).Entries);
        Assert.DoesNotContain("/login", File.ReadAllText(historyPath), StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Login_reports_a_faulted_secret_prompt_without_crashing()
    {
        const string existing =
            """{ "schemaVersion": 1, "keys": { "openrouter": "existing-key" } }""";
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            secretPrompt: new FaultedSecretPrompt("the prompt broke")
        );
        string authPath = host.Temp.File("auth.json");
        File.WriteAllText(authPath, existing);
        byte[] before = File.ReadAllBytes(authPath);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/login groq");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "login failed: InvalidOperationException: the prompt broke");

        Assert.True(host.Session.LoginTask?.IsCompletedSuccessfully);
        Assert.Equal(before, File.ReadAllBytes(authPath));
        Assert.DoesNotContain("login failed", host.ScrollbackText, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Login_reports_a_failed_save_and_rolls_back_the_key()
    {
        const string secret = "groq-secret-value";
        var prompt = new ScriptedSecretPrompt(secret);
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            secretPrompt: prompt
        );
        string authPath = host.Temp.File("auth.json");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // A directory where the file should be makes the atomic replacement fail.
        Directory.CreateDirectory(authPath);
        try
        {
            host.Console.SendText("/login groq");
            host.Console.SendEnter();
            await WaitForFrameAsync(host, "could not save the key for 'groq'");

            Assert.True(Directory.Exists(authPath));
            Assert.DoesNotContain(secret, host.Frames, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, host.ScrollbackText, StringComparison.Ordinal);
            AssertNoFileContains(host, secret);

            // The in-memory key rolled back: logout has nothing to remove.
            host.Console.SendText("/logout groq");
            host.Console.SendEnter();
            await WaitForFrameAsync(host, "no stored key for 'groq'");

            host.Console.SendText("/quit");
            host.Console.SendEnter();
            await run;

            Assert.DoesNotContain(
                "could not save the auth file",
                host.Frames,
                StringComparison.Ordinal
            );
        }
        finally
        {
            Directory.Delete(authPath);
        }
    }

    [Fact]
    public async Task Logout_reports_a_failed_save_and_keeps_the_key_in_memory()
    {
        const string stored = "stored-secret";
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        string authPath = host.Temp.File("auth.json");
        File.WriteAllText(
            authPath,
            $$"""{ "schemaVersion": 1, "keys": { "groq": "{{stored}}" } }"""
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // A directory where the file should be makes the atomic replacement fail.
        File.Delete(authPath);
        Directory.CreateDirectory(authPath);
        try
        {
            host.Console.SendText("/logout groq");
            host.Console.SendEnter();
            await WaitForFrameAsync(host, "could not save the auth file:");

            // The rollback kept the key, so the next logout still tries to remove it and fails
            // again instead of reporting that nothing is stored.
            host.Console.SendText("/logout groq");
            host.Console.SendEnter();
            host.Console.SendText("/context");
            host.Console.SendEnter();
            await WaitForFrameAsync(host, "context: ");

            host.Console.SendText("/quit");
            host.Console.SendEnter();
            await run;

            Assert.DoesNotContain(
                "no stored key for 'groq'",
                host.Frames,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(stored, host.Frames, StringComparison.Ordinal);
            Assert.DoesNotContain(stored, host.ScrollbackText, StringComparison.Ordinal);
            AssertNoFileContains(host, stored);
            Assert.True(Directory.Exists(authPath));
        }
        finally
        {
            Directory.Delete(authPath);
        }
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/logout")]
    public async Task An_unknown_provider_is_a_usage_error_listing_the_presets(string command)
    {
        var prompt = new ScriptedSecretPrompt("never-read");
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            secretPrompt: prompt
        );
        host.Console.Size = new Lunate.Tui.ConsoleSize(400, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText(command + " not-a-provider");
        host.Console.SendEnter();
        await WaitForFrameAsync(
            host,
            command == "/login"
                ? "unknown provider for /login"
                : "unknown provider 'not-a-provider'"
        );

        Assert.Contains("providers:", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains("openrouter", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains("groq", host.LastFrame, StringComparison.Ordinal);
        Assert.Empty(prompt.Prompts);
        Assert.False(File.Exists(host.Temp.File("auth.json")));
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Logout_removes_the_key_and_reports_a_live_environment_variable()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            environment: name =>
                name switch
                {
                    "LUNATE_MODEL" => "gpt-4o-mini",
                    "GROQ_API_KEY" => "environment-secret",
                    _ => null,
                }
        );
        string authPath = host.Temp.File("auth.json");
        File.WriteAllText(
            authPath,
            """{ "schemaVersion": 1, "keys": { "groq": "stored-secret" } }"""
        );
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """{ "schemaVersion": 1, "model": "gpt-4o-mini" }"""
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/logout groq");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "removed the stored key for 'groq'");

        Assert.Contains(
            "GROQ_API_KEY is still set in the environment",
            host.LastFrame,
            StringComparison.Ordinal
        );
        AuthStore store = AuthStore.Load(authPath, _ => null);
        Assert.Null(store.TryGet("groq"));
        Assert.DoesNotContain("environment-secret", host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain("stored-secret", host.Frames, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Logout_without_a_stored_key_is_a_no_op()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/logout groq");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "no stored key for 'groq'");

        Assert.DoesNotContain("still set", host.LastFrame, StringComparison.Ordinal);
        Assert.False(File.Exists(host.Temp.File("auth.json")));
        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Login_and_logout_refuse_while_a_turn_runs()
    {
        var prompt = new ScriptedSecretPrompt("never-read");
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            secretPrompt: prompt
        );
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        host.Console.SendText("/login groq");
        host.Console.SendEnter();
        host.Console.SendText("/logout groq");
        host.Console.SendEnter();
        host.Console.SendText("/favorite");
        host.Console.SendEnter();
        host.Console.SendText("/unfavorite");
        host.Console.SendEnter();
        host.Console.SendText("/help");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
            host.ScrollbackText.Contains("/unfavorite", StringComparison.Ordinal)
        );

        Assert.Empty(prompt.Prompts);
        Assert.False(File.Exists(host.Temp.File("auth.json")));
        Assert.False(File.Exists(host.Temp.File("settings.json")));
        Assert.True(host.Session.IsRunning);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Favorite_appends_the_active_model_and_preserves_unrelated_settings()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        string settingsPath = host.Temp.File("settings.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "schemaVersion": 1,
              "thinking": "high",
              "output": { "toolResultLimit": 1234 }
            }
            """
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/favorite");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "added favorite: gpt-4o-mini");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
        AgentSettings resolved = SettingsStore.Resolve(settingsPath, _ => null);
        Assert.Equal(["gpt-4o-mini"], resolved.Favorites);
        Assert.Equal(ThinkingLevel.High, resolved.Thinking);
        Assert.Equal(1234, resolved.ToolOutputLimit);
        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Favorite_refuses_a_ninth_entry_and_a_duplicate()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        string settingsPath = host.Temp.File("settings.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "schemaVersion": 2,
              "favorites": ["gpt-4o-mini", "gpt-4o", "a", "b", "c", "d", "e", "f"]
            }
            """
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        byte[] before = File.ReadAllBytes(settingsPath);

        host.Console.SendText("/favorite claude-sonnet-5-5");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "favorites are limited to 8");

        Assert.Equal(before, File.ReadAllBytes(settingsPath));
        host.Console.SendText("/favorite");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "already a favorite: gpt-4o-mini");

        Assert.Equal(before, File.ReadAllBytes(settingsPath));
        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Favorite_and_unfavorite_validate_unknown_model_ids()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        string settingsPath = host.Temp.File("settings.json");
        File.WriteAllText(settingsPath, """{ "schemaVersion": 1 }""");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/favorite not-a-model");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "unknown model: not-a-model");

        host.Console.SendText("/unfavorite not-a-model");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "unknown model: not-a-model");

        Assert.DoesNotContain(
            "favorites",
            File.ReadAllText(settingsPath),
            StringComparison.Ordinal
        );
        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Unfavorite_removes_in_place_and_reports_a_non_favourite()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            environment: name => name == "LUNATE_MODEL" ? "gpt-4o-mini" : null
        );
        string settingsPath = host.Temp.File("settings.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "schemaVersion": 2,
              "favorites": ["gpt-4o-mini", "gpt-4o", "claude-sonnet-5-5"]
            }
            """
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/unfavorite");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "removed favorite: gpt-4o-mini");
        Assert.Equal(
            ["gpt-4o", "claude-sonnet-5-5"],
            SettingsStore.Resolve(settingsPath, _ => null).Favorites
        );

        host.Console.SendText("/unfavorite gpt-4o");
        host.Console.SendEnter();
        await WaitForFavoritesAsync(host, settingsPath, ["claude-sonnet-5-5"]);

        host.Console.SendText("/unfavorite claude-sonnet-5-5");
        host.Console.SendEnter();
        await WaitForFavoritesAsync(host, settingsPath, []);

        host.Console.SendText("/unfavorite gpt-4o");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "not a favorite: gpt-4o");

        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_failed_settings_write_keeps_the_previous_list_and_reports()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        string settingsPath = host.Temp.File("settings.json");
        File.WriteAllText(settingsPath, """{ "schemaVersion": 1 }""");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // A directory where the file used to be makes the atomic replacement fail while the
        // history file and session store stay writable.
        File.Delete(settingsPath);
        Directory.CreateDirectory(settingsPath);
        try
        {
            host.Console.SendText("/favorite");
            host.Console.SendEnter();
            await WaitForFrameAsync(host, "could not save favorites:");

            Assert.True(Directory.Exists(settingsPath));

            // The in-memory list did not gain the failed entry: a duplicate would say so instead.
            host.Console.SendText("/favorite");
            host.Console.SendEnter();
            await WaitForFrameAsync(host, "could not save favorites:");
        }
        finally
        {
            Directory.Delete(settingsPath);
        }

        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Favorite_on_a_duplicate_key_settings_file_reports_and_keeps_the_pump_alive()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        string settingsPath = host.Temp.File("settings.json");
        string original = """
            {
              "schemaVersion": 1,
              "schemaVersion": 1
            }
            """;
        File.WriteAllText(settingsPath, original);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/favorite");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "could not save favorites:");

        Assert.Contains("Invalid settings", host.LastFrame, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(settingsPath));

        // The key pump survived the refused write: the next command still runs.
        host.Console.SendText("/help");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
            host.ScrollbackText.Contains("/session", StringComparison.Ordinal)
        );

        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static void AssertNoFileContains(InteractiveSessionHost host, string secret)
    {
        foreach (
            string file in Directory.EnumerateFiles(
                host.Temp.Root,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            Assert.DoesNotContain(secret, File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    private static Task WaitForFavoritesAsync(
        InteractiveSessionHost host,
        string settingsPath,
        string[] expected
    ) =>
        host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return SettingsStore.Resolve(settingsPath, _ => null).Favorites.SequenceEqual(expected);
        });

    private static Task WaitForFrameAsync(InteractiveSessionHost host, string text) =>
        host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains(text, StringComparison.Ordinal);
        });

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

    private sealed class FaultedSecretPrompt(string message) : ISecretPrompt
    {
        public Task<string?> ReadAsync(string prompt, CancellationToken cancellationToken) =>
            Task.FromException<string?>(new InvalidOperationException(message));
    }
}

using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The custom row of the <c>/auth</c> picker: four masked fields, validation before the key is
/// read, the key in <c>auth.json</c> and the provider in <c>settings.json</c>, and no half-write
/// when validation refuses.
/// </summary>
public sealed class InteractiveSessionAuthCustomTests
{
    private const string CustomLabel = "Custom OpenAI-compatible endpoint…";

    [Fact]
    public async Task The_custom_row_stores_the_key_and_the_provider()
    {
        const string secret = "local-secret-value";
        var prompt = new ScriptedSecretPrompt(
            "local",
            "http://localhost:11434/v1",
            "llama3",
            secret
        );
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            secretPrompt: prompt
        );
        string authPath = host.Temp.File("auth.json");
        string settingsPath = host.Temp.File("settings.json");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await OpenCustomRowAsync(host);

        host.Console.SendEnter();
        await WaitForFrameAsync(host, "logged in: local");

        Assert.Equal(
            [
                "Custom provider id",
                "Base URL for the custom provider",
                "Model id for the custom provider",
                "API key for local",
            ],
            prompt.Prompts
        );
        Assert.Equal(secret, AuthStore.Load(authPath, _ => null).TryGet("local"));
        AgentSettings settings = SettingsStore.Resolve(settingsPath, _ => null);
        CustomProvider provider = Assert.Single(settings.Providers);
        Assert.Equal("local", provider.Id);
        Assert.Equal("http://localhost:11434/v1", provider.BaseUrl.OriginalString);
        Assert.Equal("llama3", provider.Model);
        Assert.Equal("local/llama3", provider.CatalogName);
        Assert.Contains("/model to choose local/llama3", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, host.Frames, StringComparison.Ordinal);

        ModelCatalog catalog = HarnessFactory.LoadCatalog(settings, host.Temp.File("models.json"));
        Assert.NotNull(catalog.Find("local/llama3"));

        await DismissVerificationAsync(host, "local");
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_non_loopback_http_url_is_refused_without_reading_the_key()
    {
        var prompt = new ScriptedSecretPrompt(
            "remote",
            "http://example.com/v1",
            "model",
            "never-read"
        );
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            secretPrompt: prompt
        );
        string authPath = host.Temp.File("auth.json");
        string settingsPath = host.Temp.File("settings.json");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await OpenCustomRowAsync(host);

        host.Console.SendEnter();
        await WaitForFrameAsync(host, "custom provider refused:");

        Assert.Contains("scheme 'http'", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains("loopback", host.LastFrame, StringComparison.Ordinal);
        Assert.Equal(3, prompt.Prompts.Count);
        Assert.DoesNotContain("API key for remote", prompt.Prompts);
        Assert.False(File.Exists(authPath));
        Assert.False(File.Exists(settingsPath));
        Assert.DoesNotContain("never-read", host.Frames, StringComparison.Ordinal);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_missing_model_id_is_refused_without_reading_the_key()
    {
        var prompt = new ScriptedSecretPrompt(
            "local",
            "http://localhost:11434/v1",
            "",
            "never-read"
        );
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            secretPrompt: prompt
        );
        string authPath = host.Temp.File("auth.json");
        string settingsPath = host.Temp.File("settings.json");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await OpenCustomRowAsync(host);

        host.Console.SendEnter();
        await WaitForFrameAsync(host, "custom provider refused:");

        Assert.Contains("model id is required", host.LastFrame, StringComparison.Ordinal);
        Assert.Equal(3, prompt.Prompts.Count);
        Assert.DoesNotContain("API key for local", prompt.Prompts);
        Assert.False(File.Exists(authPath));
        Assert.False(File.Exists(settingsPath));
        Assert.DoesNotContain("never-read", host.Frames, StringComparison.Ordinal);
        host.Console.Complete();
        await run;
    }

    private static async Task OpenCustomRowAsync(InteractiveSessionHost host)
    {
        host.Console.SendText("/auth");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "Select provider");

        for (var press = 0; press < ProviderPresets.All.Count; press++)
        {
            host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        }

        await WaitForFrameAsync(host, "> " + CustomLabel);
    }

    private static async Task DismissVerificationAsync(
        InteractiveSessionHost host,
        string providerId
    )
    {
        string question = $"Verify {providerId} with one request?";
        await WaitForFrameAsync(host, question);
        host.Console.SendEscape();
        await WaitForFrameGoneAsync(host, question);
    }

    private static Task WaitForFrameAsync(InteractiveSessionHost host, string text) =>
        host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains(text, StringComparison.Ordinal);
        });

    private static Task WaitForFrameGoneAsync(InteractiveSessionHost host, string text) =>
        host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.LastFrame.Contains(text, StringComparison.Ordinal);
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
}

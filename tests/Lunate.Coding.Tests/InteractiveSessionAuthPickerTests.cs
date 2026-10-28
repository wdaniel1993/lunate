using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The <c>/auth</c> picker: every preset in file order followed by the custom row, arrow and
/// <c>j</c>/<c>k</c> navigation, Esc without a write, and a selection opening the masked prompt
/// the login path already uses. <c>/login &lt;provider&gt;</c> stays the alias without the picker.
/// </summary>
public sealed class InteractiveSessionAuthPickerTests
{
    private const string CustomLabel = "Custom OpenAI-compatible endpoint…";

    [Fact]
    public async Task Every_preset_then_the_custom_row_is_listed_with_the_first_selected()
    {
        using var host = new InteractiveSessionHost(chat: new ScriptedChatClient());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await OpenAuthPickerAsync(host);

        Assert.Contains("Select provider", host.LastFrame, StringComparison.Ordinal);
        int previous = -1;
        foreach (ProviderPreset preset in ProviderPresets.All)
        {
            string label = $"{preset.Id} — {preset.Name}";
            int index = host.LastFrame.IndexOf(label, StringComparison.Ordinal);
            Assert.True(
                index > previous,
                FormattableString.Invariant($"'{label}' is out of order")
            );
            previous = index;
        }

        Assert.Contains(
            $"> {ProviderPresets.All[0].Id} — {ProviderPresets.All[0].Name}",
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.Contains("  " + CustomLabel, host.LastFrame, StringComparison.Ordinal);
        Assert.Equal(string.Empty, host.Session.Input.Text);

        host.Console.SendEscape();
        await WaitForFrameGoneAsync(host, "Select provider");

        Assert.False(File.Exists(host.Temp.File("auth.json")));
        Assert.Empty(host.Client.Requests);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Arrows_and_j_k_move_the_selection()
    {
        using var host = new InteractiveSessionHost(chat: new ScriptedChatClient());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await OpenAuthPickerAsync(host);

        host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        await WaitForFrameAsync(host, "> anthropic — Anthropic");

        host.Console.SendText("j");
        await WaitForFrameAsync(host, "> openrouter — OpenRouter");

        host.Console.SendText("k");
        await WaitForFrameAsync(host, "> anthropic — Anthropic");

        host.Console.SendUp();
        await WaitForFrameAsync(host, "> openai — OpenAI");

        host.Console.SendEscape();
        await WaitForFrameGoneAsync(host, "Select provider");
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Escape_cancels_the_picker_without_writing()
    {
        var prompt = new ScriptedSecretPrompt("never-read");
        using var host = new InteractiveSessionHost(
            chat: new ScriptedChatClient(),
            secretPrompt: prompt
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await OpenAuthPickerAsync(host);

        host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        await WaitForFrameAsync(host, "> anthropic — Anthropic");
        host.Console.SendEscape();
        await WaitForFrameGoneAsync(host, "Select provider");

        Assert.Empty(prompt.Prompts);
        Assert.False(File.Exists(host.Temp.File("auth.json")));
        Assert.False(File.Exists(host.Temp.File("settings.json")));
        Assert.Equal(string.Empty, host.Session.Input.Text);
        Assert.Empty(host.Client.Requests);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Choosing_a_preset_opens_the_masked_prompt_and_saves_the_key()
    {
        const string secret = "groq-secret-value";
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        string authPath = host.Temp.File("auth.json");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await OpenAuthPickerAsync(host);

        int index = ProviderPresets.All.ToList().FindIndex(preset => preset.Id == "groq");
        for (var press = 0; press < index; press++)
        {
            host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        }

        await WaitForFrameAsync(host, "> groq — Groq");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "API key for groq");

        host.Console.SendText(secret);
        await WaitForFrameAsync(host, "API key for groq: " + new string('*', secret.Length));
        Assert.DoesNotContain(secret, host.Frames, StringComparison.Ordinal);

        host.Console.SendEnter();
        await WaitForFrameAsync(host, "logged in: groq");

        Assert.Contains("/model to choose", host.LastFrame, StringComparison.Ordinal);
        Assert.Equal(secret, AuthStore.Load(authPath, _ => null).TryGet("groq"));
        Assert.DoesNotContain(secret, host.Frames, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);

        await DismissVerificationAsync(host, "groq");
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Login_with_a_provider_skips_the_picker()
    {
        using var host = new InteractiveSessionHost(chat: new ScriptedChatClient());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/login groq");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "API key for groq");

        Assert.DoesNotContain("Select provider", host.Frames, StringComparison.Ordinal);

        host.Console.SendEscape();
        await WaitForFrameAsync(host, "login cancelled");

        Assert.DoesNotContain("Select provider", host.Frames, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Ordinary_input_is_inert_while_the_picker_is_open()
    {
        using var host = new InteractiveSessionHost(chat: new ScriptedChatClient());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await OpenAuthPickerAsync(host);

        host.Console.SendText("abc");
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("Select provider", StringComparison.Ordinal);
        });

        Assert.Equal(string.Empty, host.Session.Input.Text);

        // The next key still moves the picker, which proves it kept ownership of input.
        host.Console.SendText("j");
        await WaitForFrameAsync(host, "> anthropic — Anthropic");

        host.Console.SendEscape();
        await WaitForFrameGoneAsync(host, "Select provider");
        Assert.Empty(host.Client.Requests);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Auth_refuses_while_a_turn_runs_and_opens_no_picker()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        await host.Session.DispatchCommandAsync("/auth", TestContext.Current.CancellationToken);
        await WaitForFrameAsync(host, "a turn is running");

        Assert.DoesNotContain("Select provider", host.Frames, StringComparison.Ordinal);

        host.Client.Release(1);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.Complete();
        await run;
    }

    private static async Task OpenAuthPickerAsync(InteractiveSessionHost host)
    {
        host.Console.SendText("/auth");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "Select provider");
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

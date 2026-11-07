using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The rc.3 masking audit: exactly one prompt is masked — the credential key. Every non-secret
/// wizard, <c>/auth</c> and <c>/login</c> field echoes visibly. The recording prompt below is
/// driven by the real call sites (the session's <c>PromptAsync</c>, <c>SecretPromptAsync</c>,
/// <c>CustomLoginAsync</c> and <c>BeginPresetLogin</c>), not by a hand-built list, and records the
/// <c>(label, masked)</c> pair each read was opened with.
/// </summary>
[Collection("setup-wizard")]
public sealed class SecretMaskingAuditTests
{
    private const string CustomLabel = "Custom OpenAI-compatible endpoint…";

    [PosixFact]
    public async Task Only_the_credential_prompt_is_masked_across_the_wizard_auth_and_login()
    {
        var log = new List<(string Label, bool Masked)>();

        await WalkWizardCustomPathAsync(log);
        await WalkWizardCredentialReferenceAsync(log);
        await WalkAuthAndLoginAsync(log);

        Assert.NotEmpty(log);

        foreach ((string label, bool masked) in log)
        {
            bool credential = label.StartsWith("API key", StringComparison.Ordinal);
            Assert.True(
                masked == credential,
                $"'{label}' must be {(credential ? "masked" : "visible")}"
            );
        }

        // The walk genuinely reached every surface: a credential was masked and the visible
        // fields were all collected.
        Assert.Contains(log, prompt => prompt.Masked);
        Assert.True(
            log.Count(prompt => !prompt.Masked) >= 8,
            $"expected the walk to collect many visible prompts, got:\n"
                + string.Join("\n", log.Select(prompt => $"  {prompt.Label} -> {prompt.Masked}"))
        );
    }

    /// <summary>
    /// The wizard's custom-provider walk: provider id, endpoint URL, region, models URL, the typed
    /// credential, a manual model id, a profile name and the routing descriptions — the full set of
    /// the wizard's text prompts in one pass.
    /// </summary>
    private static async Task WalkWizardCustomPathAsync(List<(string Label, bool Masked)> log)
    {
        var prompt = new RecordingSecretPrompt(
            log,
            "local", // Custom provider id
            "https://local.example.test/v1", // Custom endpoint URL
            "", // Region (unset)
            "", // Models URL (skip)
            "custom-key", // API key for local (the one masked prompt)
            "any-model", // Model id
            "chat-main", // Profile name
            "for staged edit batches", // description for chat-main
            "for audits" // description for the decision profile
        );
        using var host = new InteractiveSessionHost(
            interactiveConsole: true,
            freshInstall: true,
            secretPrompt: prompt
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await host.WaitForFrameAsync("Select provider");
        for (var row = 0; row < ProviderPresets.All.Count; row++)
        {
            host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        }

        await host.WaitForFrameAsync("> " + CustomLabel);
        host.Console.SendEnter();

        // id + endpoint URL are read by the recording prompt; the family picker is next.
        await host.WaitForFrameAsync("Custom API family");
        host.Console.SendEnter(); // openai-chat

        await host.WaitForFrameAsync("Credential placement");
        host.Console.SendEnter(); // bearer

        // region + models URL are read by the recording prompt; the credential picker is next.
        await host.WaitForFrameAsync("Credential for local");
        host.Console.SendEnter(); // the "enter a key" row

        // API key (masked) is read by the recording prompt; the connection test is next.
        await SkipConnectionTestAsync(host);

        // No listing: enter the model id manually.
        await host.WaitForFrameAsync("Model (discovery listed nothing — enter an id manually)");
        host.Console.SendEnter(); // "enter a model id manually"
        await host.WaitForFrameAsync("Model check");
        host.Console.SendEnter(); // skip

        // Profile name is read by the recording prompt; fast usage is next.
        await host.WaitForFrameAsync("fast usage");
        host.Console.SendEnter(); // skip

        // Bind the decision profile as an adapter so routing has two profiles to describe.
        await host.WaitForFrameAsync(
            "Decision profile (this provider serves no native Jev profile"
        );
        host.Console.Send(new KeyEvent(KeyKind.Up, null, false, false, false));
        await host.WaitForFrameAsync("> adapt the chat model (adapter)");
        host.Console.SendEnter();

        await host.WaitForFrameAsync("Decision check");
        host.Console.SendEnter(); // skip

        await PickRowAsync(
            host,
            "Decisions settings",
            "routing unset — choose a profile per request"
        );

        await host.WaitForFrameAsync("Routing (decisions.routing)");
        host.Advance(500);
        host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        await host.WaitForFrameAsync("> shadow");
        host.Advance(500);
        host.Console.SendEnter();

        // The two descriptions are read by the recording prompt; the summary confirms.
        await host.WaitForScrollbackAsync("confirm these values");
        await host.WaitForFrameAsync("Write this configuration?");
        host.Console.SendEscape(); // abort: nothing is written
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.Complete();
        await run;
    }

    /// <summary>The credential-reference branch: a visible name (<c>env:</c>/<c>auth:</c>), not the key.</summary>
    private static async Task WalkWizardCredentialReferenceAsync(
        List<(string Label, bool Masked)> log
    )
    {
        var prompt = new RecordingSecretPrompt(log, "env:SETUP_WIZARD_TEST_KEY");
        using var host = new InteractiveSessionHost(
            interactiveConsole: true,
            freshInstall: true,
            secretPrompt: prompt
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await host.WaitForFrameAsync("Select provider");
        host.Console.SendEnter(); // openai
        await host.WaitForFrameAsync("Credential for openai");
        host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        await host.WaitForFrameAsync("> name an env: or auth: reference instead");
        host.Console.SendEnter();

        // The reference is read by the recording prompt; the connection test opens next.
        await host.WaitForFrameAsync("Connection test (one discovery request");
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.Complete();
        await run;
    }

    /// <summary><c>/auth</c> custom and preset, and <c>/login</c>: three surfaces, one masked key each.</summary>
    private static async Task WalkAuthAndLoginAsync(List<(string Label, bool Masked)> log)
    {
        var prompt = new RecordingSecretPrompt(
            log,
            "local", // /auth custom provider id
            "http://localhost:11434/v1", // /auth custom base URL
            "llama3", // /auth custom model id
            "custom-key", // /auth custom API key
            "groq-key", // /auth preset API key
            "groq-key-two" // /login API key
        );
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient(),
            secretPrompt: prompt
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await OpenCustomRowAsync(host);
        host.Console.SendEnter();
        await host.WaitForFrameAsync("logged in: local");
        await DismissVerificationAsync(host, "local");

        await OpenAuthPickerAsync(host);
        await PickRowAsync(host, "Select provider", "groq — Groq");
        await host.WaitForFrameAsync("logged in: groq");
        await DismissVerificationAsync(host, "groq");

        host.Console.SendText("/login groq");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("logged in: groq");
        await DismissVerificationAsync(host, "groq");

        host.Console.Complete();
        await run;
    }

    private static async Task OpenAuthPickerAsync(InteractiveSessionHost host)
    {
        host.Console.SendText("/auth");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Select provider");
    }

    private static async Task OpenCustomRowAsync(InteractiveSessionHost host)
    {
        await OpenAuthPickerAsync(host);
        for (var press = 0; press < ProviderPresets.All.Count; press++)
        {
            host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        }

        await host.WaitForFrameAsync("> " + CustomLabel);
    }

    private static async Task SkipConnectionTestAsync(InteractiveSessionHost host)
    {
        await host.WaitForFrameAsync("Connection test (one discovery request");
        host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        await host.WaitForFrameAsync("> Skip the connection test");
        host.Console.SendEnter();
    }

    private static async Task PickRowAsync(InteractiveSessionHost host, string title, string row)
    {
        await host.WaitForFrameAsync(title);
        string previous = host.LastFrame;
        while (!host.LastFrame.Contains($"> {row}", StringComparison.Ordinal))
        {
            host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
            await host.WaitUntilAsync(() =>
            {
                host.Advance(33);
                return host.LastFrame != previous;
            });
            previous = host.LastFrame;
        }

        host.Console.SendEnter();
    }

    private static async Task DismissVerificationAsync(
        InteractiveSessionHost host,
        string providerId
    )
    {
        string question = $"Verify {providerId} with one request?";
        await host.WaitForFrameAsync(question);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.LastFrame.Contains(question, StringComparison.Ordinal);
        });
    }

    /// <summary>Records <c>(label, masked)</c> for every read and answers from a fixed queue.</summary>
    private sealed class RecordingSecretPrompt(
        List<(string Label, bool Masked)> log,
        params string?[] answers
    ) : ISecretPrompt
    {
        private readonly Queue<string?> _answers = new(answers);
        private readonly Lock _gate = new();

        public Task<string?> ReadAsync(string prompt, CancellationToken cancellationToken) =>
            Record(prompt, masked: true);

        public Task<string?> ReadTextAsync(string prompt, CancellationToken cancellationToken) =>
            Record(prompt, masked: false);

        private Task<string?> Record(string prompt, bool masked)
        {
            lock (_gate)
            {
                log.Add((prompt, masked));
                return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : null);
            }
        }
    }
}

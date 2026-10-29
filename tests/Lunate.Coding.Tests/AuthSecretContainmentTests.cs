using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// Task 5.3's containment proof: the key typed on any login path reaches <c>auth.json</c> and
/// nothing else — not a notice, a frame, the scrollback, the history file or a session file.
/// </summary>
public sealed class AuthSecretContainmentTests
{
    private const string Secret = "containment-secret-value";
    private const string CustomLabel = "Custom OpenAI-compatible endpoint…";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cli_login_writes_the_key_only_to_the_auth_store(bool terminal)
    {
        using var temp = new TempDirectory();
        string authPath = temp.File("auth.json");
        AuthCommandSeams seams = Seams(temp, terminal, new ScriptedSecretPrompt(Secret));
        string[] args = ["login", "groq"];
        if (!terminal)
        {
            args = ["login", "groq", "--key-stdin"];
            seams = seams with { Input = new StringReader(Secret + "\n") };
        }

        (int exit, string output, string errors) = Run(args, seams);

        Assert.Equal(0, exit);
        Assert.Equal(Secret, AuthStore.Load(authPath, _ => null).Keys["groq"]);
        Assert.DoesNotContain(Secret, output + errors, StringComparison.Ordinal);
        AssertSecretOnlyInAuth(temp.Root, authPath);
    }

    [Fact]
    public async Task The_auth_picker_never_commits_the_key_to_a_notice_history_or_session()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
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

        await TypeSecretAsync(host, "API key for groq");
        await WaitForFrameAsync(host, "logged in: groq");

        Assert.Equal(Secret, AuthStore.Load(host.Temp.File("auth.json"), _ => null).TryGet("groq"));
        AssertSecretNowhereButAuth(host);

        await DismissVerificationAsync(host, "groq");
        await QuitAsync(host, run);
    }

    [Fact]
    public async Task The_first_run_offer_never_commits_the_key_to_a_notice_history_or_session()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        host.Console.IsInteractive = true;
        Task run = host.RunAsync();
        await WaitForFrameAsync(host, "Select provider");

        host.Console.SendEnter();
        await WaitForFrameAsync(host, "API key for openai");

        await TypeSecretAsync(host, "API key for openai");
        await WaitForFrameAsync(host, "logged in: openai");

        Assert.Equal(
            Secret,
            AuthStore.Load(host.Temp.File("auth.json"), _ => null).TryGet("openai")
        );
        AssertSecretNowhereButAuth(host);

        await DismissVerificationAsync(host, "openai");
        await QuitAsync(host, run);
    }

    [Fact]
    public async Task The_custom_row_never_commits_the_key_to_a_notice_history_or_session()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await OpenAuthPickerAsync(host);

        for (var press = 0; press < ProviderPresets.All.Count; press++)
        {
            host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        }

        await WaitForFrameAsync(host, "> " + CustomLabel);
        host.Console.SendEnter();

        await TypeFieldAsync(host, "Custom provider id", "local");
        await TypeFieldAsync(host, "Base URL for the custom provider", "http://localhost:11434/v1");
        await TypeFieldAsync(host, "Model id for the custom provider", "llama3");
        await TypeFieldAsync(host, "API key for local", Secret);
        await WaitForFrameAsync(host, "logged in: local");

        Assert.Equal(
            Secret,
            AuthStore.Load(host.Temp.File("auth.json"), _ => null).TryGet("local")
        );
        AssertSecretNowhereButAuth(host);

        await DismissVerificationAsync(host, "local");
        await QuitAsync(host, run);
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
        bool stdinIsTerminal,
        ISecretPrompt prompt
    ) =>
        new()
        {
            AuthPath = temp.File("auth.json"),
            SettingsPath = temp.File("settings.json"),
            ModelsPath = temp.File("models.json"),
            Environment = _ => null,
            SecretPrompt = prompt,
            StdinIsTerminal = stdinIsTerminal,
        };

    private static async Task OpenAuthPickerAsync(InteractiveSessionHost host)
    {
        host.Console.SendText("/auth");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "Select provider");
    }

    private static async Task TypeSecretAsync(InteractiveSessionHost host, string label)
    {
        host.Console.SendText(Secret);
        await WaitForFrameAsync(host, label + ": " + new string('*', Secret.Length));
        host.Console.SendEnter();
    }

    private static async Task TypeFieldAsync(
        InteractiveSessionHost host,
        string label,
        string value
    )
    {
        await WaitForFrameAsync(host, label);
        host.Console.SendText(value);
        await WaitForFrameAsync(host, label + ": " + new string('*', value.Length));
        host.Console.SendEnter();
    }

    private static async Task DismissVerificationAsync(
        InteractiveSessionHost host,
        string providerId
    )
    {
        string question = $"Verify {providerId} with one request?";
        await WaitForFrameAsync(host, question);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.LastFrame.Contains(question, StringComparison.Ordinal);
        });
    }

    private static async Task QuitAsync(InteractiveSessionHost host, Task run)
    {
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static void AssertSecretNowhereButAuth(InteractiveSessionHost host)
    {
        string authPath = host.Temp.File("auth.json");
        Assert.True(File.Exists(authPath));
        Assert.DoesNotContain(Secret, host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, host.Scrollback.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, HistoryText(host), StringComparison.Ordinal);

        string[] sessionFiles = SessionFiles(host);
        Assert.NotEmpty(sessionFiles);
        foreach (string file in sessionFiles)
        {
            Assert.DoesNotContain(Secret, File.ReadAllText(file), StringComparison.Ordinal);
        }

        AssertSecretOnlyInAuth(host.Temp.Root, authPath);
    }

    /// <summary>Every file under the temp tree except the one secret store lacks the key.</summary>
    private static void AssertSecretOnlyInAuth(string root, string authPath)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(file, authPath, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.DoesNotContain(Secret, File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    private static string HistoryText(InteractiveSessionHost host)
    {
        string path = host.Temp.File("history");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static string[] SessionFiles(InteractiveSessionHost host)
    {
        string directory = host.Temp.File("sessions");
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.jsonl", SearchOption.AllDirectories)
            : [];
    }

    private static Task WaitForFrameAsync(InteractiveSessionHost host, string text) =>
        host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains(text, StringComparison.Ordinal);
        });

    private sealed class ScriptedSecretPrompt(params string?[] values) : ISecretPrompt
    {
        private readonly Queue<string?> _values = new(values);

        public Task<string?> ReadAsync(string prompt, CancellationToken cancellationToken) =>
            Task.FromResult(_values.Count > 0 ? _values.Dequeue() : null);
    }
}

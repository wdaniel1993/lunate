using Lunate.Agent;
using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The production masked prompt behind <c>/login</c>: the live area shows one asterisk per typed
/// character, Enter saves through the auth store, and the secret never reaches a frame, the
/// scrollback, the history file or the session file. The hosts here use the default prompt.
/// </summary>
public sealed class InteractiveSessionSecretPromptTests
{
    private const string Secret = "groq-secret-value";

    [Fact]
    public async Task Login_masks_the_key_without_echoing_it()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        string authPath = host.Temp.File("auth.json");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await OpenLoginAsync(host);

        host.Console.SendText(Secret);
        await WaitForFrameAsync(host, "API key for groq: " + new string('*', 17));

        Assert.Contains(
            "API key for groq: " + new string('*', 17),
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(Secret, host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, host.ScrollbackText, StringComparison.Ordinal);

        host.Console.SendEnter();
        await WaitForFrameAsync(host, "logged in: groq");

        AuthStore store = AuthStore.Load(authPath, _ => null);
        Assert.Equal(Secret, store.TryGet("groq"));
        Assert.DoesNotContain(Secret, host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, host.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, HistoryText(host), StringComparison.Ordinal);
        string[] sessionFiles = SessionFiles(host);
        Assert.NotEmpty(sessionFiles);
        foreach (string file in sessionFiles)
        {
            Assert.DoesNotContain(Secret, File.ReadAllText(file), StringComparison.Ordinal);
        }

        Assert.Equal(string.Empty, host.Session.Input.Text);
        Assert.Empty(host.Client.Requests);

        await QuitAsync(host, run);
    }

    [Fact]
    public async Task Escape_cancels_without_saving_a_partial_key()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        string authPath = host.Temp.File("auth.json");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await OpenLoginAsync(host);

        host.Console.SendText("groq-part");
        await WaitForFrameAsync(host, "API key for groq: *********");
        host.Console.SendEscape();
        await WaitForFrameAsync(host, "login cancelled");

        Assert.False(File.Exists(authPath));
        Assert.DoesNotContain("groq-part", host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain("groq-part", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Equal(string.Empty, host.Session.Input.Text);
        Assert.Empty(host.Client.Requests);

        await QuitAsync(host, run);
    }

    [Fact]
    public async Task Ordinary_input_is_consumed_by_the_open_prompt()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        string authPath = host.Temp.File("auth.json");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await OpenLoginAsync(host);

        host.Console.SendText("not a command");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "logged in: groq");

        AuthStore store = AuthStore.Load(authPath, _ => null);
        Assert.Equal("not a command", store.TryGet("groq"));
        Assert.DoesNotContain("not a command", host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain("not a command", host.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain("not a command", HistoryText(host), StringComparison.Ordinal);
        string[] sessionFiles = SessionFiles(host);
        Assert.NotEmpty(sessionFiles);
        foreach (string file in sessionFiles)
        {
            Assert.DoesNotContain(
                "\"type\":\"message\"",
                File.ReadAllText(file),
                StringComparison.Ordinal
            );
        }

        Assert.Equal(string.Empty, host.Session.Input.Text);
        Assert.Empty(host.Client.Requests);

        await QuitAsync(host, run);
    }

    [Fact]
    public async Task Backspace_shortens_the_mask_before_saving()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await OpenLoginAsync(host);

        host.Console.SendText("abcdefgh");
        await WaitForFrameAsync(host, "API key for groq: ********\n");
        host.Console.Send(new KeyEvent(KeyKind.Backspace, null, false, false, false));
        await WaitForFrameAsync(host, "API key for groq: *******\n");
        Assert.DoesNotContain(
            "API key for groq: " + new string('*', 8) + "\n",
            host.LastFrame,
            StringComparison.Ordinal
        );

        host.Console.SendEnter();
        await WaitForFrameAsync(host, "logged in: groq");

        AuthStore store = AuthStore.Load(host.Temp.File("auth.json"), _ => null);
        Assert.Equal("abcdefg", store.TryGet("groq"));
        Assert.DoesNotContain("abcdefg", host.Frames, StringComparison.Ordinal);

        await QuitAsync(host, run);
    }

    private static async Task OpenLoginAsync(InteractiveSessionHost host)
    {
        host.Console.SendText("/login groq");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "API key for groq");
    }

    private static async Task QuitAsync(InteractiveSessionHost host, Task run)
    {
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
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
}

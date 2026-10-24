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

        // The masked frame is the event; the typed secret must not appear on the surface it left.
        string scrollback = await host.ScrollbackAfterAsync(() =>
            WaitForFrameAsync(host, "API key for groq: " + new string('*', 17))
        );

        Assert.Contains(
            "API key for groq: " + new string('*', 17),
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(Secret, host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, scrollback, StringComparison.Ordinal);

        host.Console.SendEnter();

        // The login's confirmation is the event; the secret must not have been committed.
        string loggedIn = await host.ScrollbackAfterAsync(() =>
            WaitForFrameAsync(host, "logged in: groq")
        );

        AuthStore store = AuthStore.Load(authPath, _ => null);
        Assert.Equal(Secret, store.TryGet("groq"));
        Assert.DoesNotContain(Secret, host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, loggedIn, StringComparison.Ordinal);
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

        // The cancellation notice is the event; the partial key must not appear on that surface.
        string scrollback = await host.ScrollbackAfterAsync(() =>
            WaitForFrameAsync(host, "login cancelled")
        );

        Assert.False(File.Exists(authPath));
        Assert.DoesNotContain("groq-part", host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain("groq-part", scrollback, StringComparison.Ordinal);
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

        // The secret prompt never echoes its input, so the text must be absent from the surface
        // the login's confirmation settled.
        string scrollback = await host.ScrollbackAfterAsync(() =>
            WaitForFrameAsync(host, "logged in: groq")
        );

        AuthStore store = AuthStore.Load(authPath, _ => null);
        Assert.Equal("not a command", store.TryGet("groq"));
        Assert.DoesNotContain("not a command", host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain("not a command", scrollback, StringComparison.Ordinal);
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

    [Fact]
    public async Task A_pasted_key_is_masked_and_saved()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        string authPath = host.Temp.File("auth.json");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await OpenLoginAsync(host);

        // The paste carries a trailing newline: it is stripped, not treated as Enter.
        host.Console.Send(new KeyEvent(KeyKind.Paste, Secret + "\r\n ", false, false, false));
        await WaitForFrameAsync(host, "API key for groq: " + new string('*', Secret.Length));
        Assert.False(File.Exists(authPath));

        host.Console.SendEnter();

        // The login's confirmation is the event; the pasted secret must not have been committed.
        string scrollback = await host.ScrollbackAfterAsync(() =>
            WaitForFrameAsync(host, "logged in: groq")
        );

        AuthStore store = AuthStore.Load(authPath, _ => null);
        Assert.Equal(Secret, store.TryGet("groq"));
        Assert.DoesNotContain(Secret, host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, scrollback, StringComparison.Ordinal);
        AssertSecretOnlyInAuth(host, authPath);

        await QuitAsync(host, run);
    }

    [Fact]
    public async Task Cancelling_a_pending_prompt_closes_it_without_repainting_it()
    {
        var console = new ScriptedConsoleIO();
        var scheduler = new ManualScheduler();
        using var live = new LiveArea(console, scheduler, readKeys: false);
        live.Start();
        var prompt = new TuiSecretPrompt(live);

        // Cancel races the key pump: whichever wins, no typed text may survive a detach.
        for (int i = 0; i < 50; i++)
        {
            using var cancellation = new CancellationTokenSource();
            Task<string?> read = prompt.ReadAsync("API key for groq", cancellation.Token);
            Assert.True(prompt.IsOpen);
            await Task.WhenAll(
                Task.Run(
                    () =>
                        prompt.HandleKey(new KeyEvent(KeyKind.Character, "x", false, false, false)),
                    TestContext.Current.CancellationToken
                ),
                Task.Run(cancellation.Cancel, TestContext.Current.CancellationToken)
            );

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            Assert.False(prompt.IsOpen);
        }

        scheduler.Advance(TimeSpan.FromMilliseconds(33));
        Assert.DoesNotContain("API key for groq", console.Writes[^1], StringComparison.Ordinal);
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

    private static void AssertSecretOnlyInAuth(InteractiveSessionHost host, string authPath)
    {
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
                Assert.DoesNotContain(Secret, File.ReadAllText(file), StringComparison.Ordinal);
            }
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
}

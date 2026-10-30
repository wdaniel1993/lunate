using System.Text.Json;
using System.Text.RegularExpressions;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

[CollectionDefinition("interactive-session-tui-settings", DisableParallelization = true)]
public sealed class InteractiveSessionTuiSettingsCollection
{
    public const string Name = "interactive-session-tui-settings";
}

/// <summary>
/// The TUI settings in a running session: <c>/settings tui.theme</c> applies the theme's colour
/// plan to the live frame and <c>/settings tui.keys</c> rewrites the binding's declared keys in
/// place — both persist through the settings file, so both survive a restart. Every drive of a
/// remapped key runs through the real key pump and the real catalogue.
/// </summary>
/// <remarks>
/// The catalogue is static, so a test that remaps restores the shipped keys around itself; the
/// restart test restores before composing the second host, because a fresh session must re-apply
/// the remaps from the file rather than inherit the first host's in-memory state.
/// </remarks>
[Collection(InteractiveSessionTuiSettingsCollection.Name)]
public sealed class InteractiveSessionTuiSettingsTests
{
    [Fact]
    public async Task The_theme_edit_applies_persists_and_shows_in_the_settings_table()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await host.SendLineAsync("/settings tui.theme mono");
        await WaitForFrameAsync(host, "tui.theme: mono");

        AgentSettings persisted = SettingsStore.Resolve(host.Temp.File("settings.json"), _ => null);
        Assert.Equal("mono", persisted.Tui.Theme);

        host.Console.SendText("/settings");
        host.Console.SendEnter();
        string scrollback = await host.WaitForScrollbackAsync("tui.theme       mono");
        Assert.Contains("tui.keys        none", scrollback, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task An_unknown_theme_is_refused_naming_the_literals()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await host.SendLineAsync("/settings tui.theme neon");
        await WaitForFrameAsync(host, "tui.theme must be 'default', 'ansi16' or 'mono'");

        // The refusal left nothing behind: the settings file holds no tui section.
        Assert.False(File.Exists(host.Temp.File("settings.json")));

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_mono_theme_drops_every_attribute_from_the_live_frame()
    {
        using var host = new InteractiveSessionHost(
            interactiveConsole: true,
            environment: name =>
                name switch
                {
                    "COLORTERM" => "truecolor",
                    "OPENAI_API_KEY" => "test-key",
                    _ => PrintModeTestSupport.Environment()(name),
                }
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // The colour-capable placeholder carries its prompt tone; nothing is asserted until a
        // frame proves the session actually painted with it.
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("\u001b[38;2;135;175;215m", StringComparison.Ordinal);
        });

        await host.SendLineAsync("/settings tui.theme mono");
        host.Console.SendCtrlL();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> Ask anything", StringComparison.Ordinal)
                && !Regex.IsMatch(host.LastFrame, @"\x1b\[[0-9;]*m", RegexOptions.CultureInvariant);
        });

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Theory]
    [InlineData(
        "/settings tui.keys nope ctrl+t",
        "nope is not a binding id; the closest names are"
    )]
    [InlineData("/settings tui.keys ctrl-d.quit ctrl+j", "newline.ctrl-j")]
    [InlineData("/settings tui.keys ctrl-d.quit banana", "'banana' is not a key")]
    [InlineData("/settings tui.keys ctrl-d.quit", "tui.keys takes a binding id and a key")]
    public async Task A_remapped_key_that_cannot_route_is_refused_without_persisting(
        string command,
        string expected
    )
    {
        IReadOnlyList<(string Id, KeyPattern[] Keys)> before = KeyBindingCatalog.Snapshot();
        (string Id, KeyPattern[] Keys) declared = before.First(row => row.Id == "ctrl-d.quit");
        try
        {
            using var host = new InteractiveSessionHost();
            Task run = host.RunAsync();
            await host.WaitUntilAsync(() => host.Session.IsComposed);
            string settingsPath = host.Temp.File("settings.json");
            Assert.False(File.Exists(settingsPath));

            await host.SendLineAsync(command);
            await WaitForFrameAsync(host, expected);

            // Nothing was persisted and the binding's declared keys are unchanged.
            Assert.False(File.Exists(settingsPath));
            Assert.Equal(
                declared.Keys,
                KeyBindingCatalog.BindingsFor("ctrl-d.quit").Select(row => row.Key).ToArray()
            );

            host.Console.SendText("/quit");
            host.Console.SendEnter();
            await run;
        }
        finally
        {
            Restore(before);
        }
    }

    [Fact]
    public async Task The_remapped_key_drives_its_action_this_session_and_after_a_restart()
    {
        IReadOnlyList<(string Id, KeyPattern[] Keys)> before = KeyBindingCatalog.Snapshot();
        string persisted;
        try
        {
            persisted = await RemapAndPersistThroughFirstSession();
            Restore(before);

            using var host = new InteractiveSessionHost();
            File.WriteAllText(host.Temp.File("settings.json"), persisted);
            Task run = host.RunAsync();
            await host.WaitUntilAsync(() => host.Session.IsComposed);

            // The restart applied the persisted remap: the shipped Ctrl+O is dead...
            Assert.NotEqual(RoutedKey.TranscriptViewer, KeyRouter.Route(Ctrl("o")));
            // ...and the new key drives the binding through the real key pump.
            await host.SendLineAsync("/settings");
            string scrollback = await host.WaitForScrollbackAsync(
                "tui.keys        ctrl-o.transcript=Ctrl+T"
            );
            Assert.Contains("tui.theme       mono", scrollback, StringComparison.Ordinal);

            host.Console.SendCtrl("t");
            await host.WaitUntilAsync(() => host.Console.InAlternateScreen);
            await host.WaitUntilAsync(() =>
                host.Console.Writes.Any(write =>
                    write.Contains("PgUp/PgDn", StringComparison.Ordinal)
                )
            );
            host.Console.SendEscape();
            await host.WaitUntilAsync(
                () => !host.Console.InAlternateScreen,
                InteractiveSessionHost.DefaultWait
            );

            host.Console.SendText("/quit");
            host.Console.SendEnter();
            await run;
        }
        finally
        {
            Restore(before);
        }
    }

    /// <summary>The first session: choose the theme, remap the transcript key, quit, return the
    /// settings file exactly as the session saved it.</summary>
    private static async Task<string> RemapAndPersistThroughFirstSession()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await host.SendLineAsync("/settings tui.theme mono");
        await WaitForFrameAsync(host, "tui.theme: mono");

        await host.SendLineAsync("/settings tui.keys ctrl-o.transcript ctrl+t");
        await WaitForFrameAsync(host, "tui.keys: ctrl-o.transcript → Ctrl+T");

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;

        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(host.Temp.File("settings.json"))
        );
        Assert.Equal(
            "mono",
            document.RootElement.GetProperty("tui").GetProperty("theme").GetString()
        );
        return File.ReadAllText(host.Temp.File("settings.json"));
    }

    private static void Restore(IReadOnlyList<(string Id, KeyPattern[] Keys)> snapshot)
    {
        foreach ((string id, KeyPattern[] keys) in snapshot)
        {
            KeyPattern[] current = [.. KeyBindingCatalog.BindingsFor(id).Select(row => row.Key)];
            if (!current.SequenceEqual(keys))
            {
                Assert.Null(KeyBindingCatalog.Remap(id, keys));
            }
        }
    }

    private static KeyEvent Ctrl(string text) =>
        new(KeyKind.Character, text, Ctrl: true, Shift: false, Alt: false);

    private static Task WaitForFrameAsync(InteractiveSessionHost host, string text) =>
        host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains(text, StringComparison.Ordinal);
        });
}

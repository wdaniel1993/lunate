using Lunate.Agent;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// Ctrl+P cycles the settings favourites through the model-switch path, wrapping at the end; a
/// non-favourite starts at the first, an empty list is a notice, and a running turn or an open
/// picker refuses the switch.
/// </summary>
public sealed class InteractiveSessionFavoritesTests
{
    private static readonly KeyEvent CtrlP = new(
        KeyKind.Character,
        "p",
        Ctrl: true,
        Shift: false,
        Alt: false
    );

    [Fact]
    public async Task Ctrl_P_advances_through_the_favourites_and_wraps()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" }
        );
        string settingsPath = host.Temp.File("settings.json");
        SettingsStore.WriteFavorites(["gpt-4o-mini", "gpt-4o", "claude-sonnet-5-5"], settingsPath);
        Assert.Equal(
            ["gpt-4o-mini", "gpt-4o", "claude-sonnet-5-5"],
            SettingsStore.Resolve(settingsPath, _ => null).Favorites
        );

        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.Send(CtrlP);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: gpt-4o");
        host.Console.Send(CtrlP);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: claude-sonnet-5-5");
        host.Console.Send(CtrlP);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: gpt-4o-mini");

        Assert.Equal(
            ["gpt-4o", "claude-sonnet-5-5", "gpt-4o-mini"],
            LoadedSession(host)
                .Entries.OfType<SessionModelChangeEntry>()
                .Select(change => change.Model)
        );
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_non_favourite_starts_at_the_first()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" }
        );
        string settingsPath = host.Temp.File("settings.json");
        SettingsStore.WriteFavorites(["gpt-4o", "claude-sonnet-5-5"], settingsPath);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.Send(CtrlP);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: gpt-4o");

        SessionModelChangeEntry change = Assert.Single(
            LoadedSession(host).Entries.OfType<SessionModelChangeEntry>()
        );
        Assert.Equal("gpt-4o", change.Model);
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task An_empty_list_is_a_notice_without_a_switch_or_run()
    {
        using var host = new InteractiveSessionHost();
        string settingsPath = host.Temp.File("settings.json");
        SettingsStore.WriteFavorites([], settingsPath);
        Assert.Empty(SettingsStore.Resolve(settingsPath, _ => null).Favorites);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.Send(CtrlP);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "/favorite");

        Assert.Empty(LoadedSession(host).Entries.OfType<SessionModelChangeEntry>());
        Assert.Empty(host.Client.Requests);
        Assert.False(host.Session.IsRunning);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Ctrl_P_skips_an_unresolvable_favourite_and_reports_it_once()
    {
        using var host = Host("fixture-c");
        SettingsStore.WriteFavorites(
            ["dead", "fixture-a", "fixture-b"],
            host.Temp.File("settings.json")
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.Send(CtrlP);
        await WaitForModelChangesAsync(host, 1);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "not in the catalog");
        Assert.Contains("dead", host.LastFrame, StringComparison.Ordinal);

        host.Console.Send(CtrlP);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: fixture-b");
        Assert.DoesNotContain("not in the catalog", host.LastFrame, StringComparison.Ordinal);

        host.Console.Send(CtrlP);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: fixture-a");
        Assert.DoesNotContain("not in the catalog", host.LastFrame, StringComparison.Ordinal);

        Assert.Equal(
            ["fixture-a", "fixture-b", "fixture-a"],
            LoadedSession(host)
                .Entries.OfType<SessionModelChangeEntry>()
                .Select(change => change.Model)
        );
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Ctrl_P_skips_a_dead_favourite_between_the_active_model_and_the_next()
    {
        using var host = Host("fixture-a");
        SettingsStore.WriteFavorites(
            ["fixture-a", "dead", "fixture-b"],
            host.Temp.File("settings.json")
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.Send(CtrlP);
        await WaitForModelChangesAsync(host, 1);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "not in the catalog");
        Assert.Contains("dead", host.LastFrame, StringComparison.Ordinal);

        host.Console.Send(CtrlP);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: fixture-a");
        Assert.DoesNotContain("not in the catalog", host.LastFrame, StringComparison.Ordinal);

        Assert.Equal(
            ["fixture-b", "fixture-a"],
            LoadedSession(host)
                .Entries.OfType<SessionModelChangeEntry>()
                .Select(change => change.Model)
        );
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Ctrl_P_waits_for_idle_and_never_switches_mid_turn()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" }
        );
        string settingsPath = host.Temp.File("settings.json");
        SettingsStore.WriteFavorites(["gpt-4o"], settingsPath);
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        host.Console.Send(CtrlP);
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "a turn is running — Esc to cancel first"
        );

        Assert.Empty(LoadedSession(host).Entries.OfType<SessionModelChangeEntry>());

        host.Client.Release(1);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Ctrl_P_is_inert_while_the_model_picker_is_open()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" }
        );
        string settingsPath = host.Temp.File("settings.json");
        SettingsStore.WriteFavorites(["gpt-4o"], settingsPath);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.Send(
            new KeyEvent(KeyKind.Character, "l", Ctrl: true, Shift: false, Alt: false)
        );
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select model");

        // The picker owns every key: Ctrl+P neither switches the model nor closes the list, and
        // the next Down still repaints the selection inside it.
        string before = host.LastFrame;
        host.Console.Send(CtrlP);
        host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !string.Equals(host.LastFrame, before, StringComparison.Ordinal);
        });

        Assert.Contains("Select model", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("model: gpt-4o", host.Frames, StringComparison.Ordinal);
        Assert.Equal(string.Empty, host.Session.Input.Text);
        Assert.Empty(LoadedSession(host).Entries.OfType<SessionModelChangeEntry>());

        host.Console.SendEscape();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "Select model");

        Assert.Empty(LoadedSession(host).Entries.OfType<SessionModelChangeEntry>());
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Ctrl_P_wins_over_an_open_autocomplete_list()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" }
        );
        string settingsPath = host.Temp.File("settings.json");
        SettingsStore.WriteFavorites(["gpt-4o"], settingsPath);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/comp");
        await host.WaitUntilAsync(() => host.Session.Autocomplete is not null);

        host.Console.Send(CtrlP);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: gpt-4o");

        Assert.Equal("/comp", host.Session.Input.Text);
        Assert.Empty(host.Client.Requests);

        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private const string Models = """
        {
          "schemaVersion": 2,
          "models": [
            {
              "id": "fixture-a",
              "provider": "fixture",
              "contextWindow": 128000,
              "supportsTools": true
            },
            {
              "id": "fixture-b",
              "provider": "fixture",
              "contextWindow": 128000,
              "supportsTools": true
            },
            {
              "id": "fixture-c",
              "provider": "fixture",
              "contextWindow": 128000,
              "supportsTools": true
            }
          ]
        }
        """;

    private static InteractiveSessionHost Host(string model)
    {
        var host = new InteractiveSessionHost(launch: new LaunchOptions { Model = model });
        File.WriteAllText(host.Temp.File("models.json"), Models);
        return host;
    }

    private static async Task WaitForModelChangesAsync(InteractiveSessionHost host, int count)
    {
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return LoadedSession(host).Entries.OfType<SessionModelChangeEntry>().Count() == count;
        });
    }

    private static Session LoadedSession(InteractiveSessionHost host) =>
        Session.Load(Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl")));
}

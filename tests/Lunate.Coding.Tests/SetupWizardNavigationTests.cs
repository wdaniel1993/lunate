using System.Net;
using System.Text.Json;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The walk's navigation (spec: The walk is navigable): a visible four-stage path, Back with every
/// answer kept, Forward replaying only answered steps, a menu that returns to itself with a Done
/// row, and a multi-select with a visible count. The tests drive key sequences and assert the
/// staged model and the written stores, never the prose of a prompt.
/// </summary>
[Collection("setup-wizard")]
public sealed class SetupWizardNavigationTests
{
    private const string Credential = "navigation-key-never-echoed";

    private const string DiscoveryBody =
        """{"data":[{"id":"nav-model","limit":{"context":32000},"capabilities":{"reasoning":{"supported":false}}}]}""";

    private static HttpResponseMessage Response(int status, string body) =>
        new((HttpStatusCode)status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    private static string SettingsPath(InteractiveSessionHost host) =>
        AuthStore.SettingsFilePath(host.Temp.File("auth.json"));

    private static KeyEvent Down() => new(KeyKind.Down, null, false, false, false);

    /// <summary>Collapses the terminal's wraps and repaints into one readable string.</summary>
    private static string Flatten(string text) =>
        string.Join(' ', text.Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    private static KeyEvent Key(KeyKind kind) => new(kind, null, false, false, false);

    [PosixFact]
    public async Task Back_from_the_summary_changes_two_answers_and_done_returns_to_it()
    {
        using var host = NavigationHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await WalkToSummaryAsync(host);
        await host.WaitForFrameAsync("Write this configuration?");

        // Left at the summary is Back into the walk, not cancel: the walk re-opens its last step.
        host.Console.Send(Key(KeyKind.Left));
        await host.WaitForFrameAsync("Decisions settings");

        // Change one answer: the approval gate.
        await PickRowAsync(host, "approval gate (decisions.approvals)");
        await host.WaitForFrameAsync("The approval gate (decisions.approvals)");
        await AnswerSwitchAsync(host, "> shadow");
        await host.WaitForFrameAsync("approval gate (decisions.approvals) — shadow");

        // Change a second answer: the decide tool. Both changes stay visible on the same menu.
        await PickRowAsync(host, "decide tool (decisions.tool)");
        await host.WaitForFrameAsync("Offers the decide tool to the model");
        await AnswerSwitchAsync(host, "> on");
        await host.WaitForFrameAsync("decide tool (decisions.tool) — on");

        // Only Done leaves the menu.
        for (var up = 0; up < 4; up++)
        {
            host.Console.Send(Key(KeyKind.Up));
        }

        await host.WaitForFrameAsync("> Done");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Write this configuration?");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("setup written");

        host.Console.Complete();
        await run;

        using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(SettingsPath(host)));
        JsonElement decisions = settings.RootElement.GetProperty("decisions");
        Assert.Equal("shadow", decisions.GetProperty("approvals").GetString());
        Assert.Equal("on", decisions.GetProperty("tool").GetString());
        Assert.Equal(
            "chat-main",
            settings.RootElement.GetProperty("usages").GetProperty("chat").GetString()
        );
    }

    [PosixFact]
    public async Task Forward_replays_the_answered_steps_and_returns_to_the_summary()
    {
        using var host = NavigationHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await WalkToSummaryAsync(host);
        await host.WaitForFrameAsync("Write this configuration?");

        // Back to the decisions menu, then one more Back to the decision-profile step.
        host.Console.Send(Key(KeyKind.Left));
        await host.WaitForFrameAsync("Decisions settings");
        host.Console.Send(new KeyEvent(KeyKind.Character, "b", false, false, false));
        await host.WaitForFrameAsync("Decision profile");

        int before = host.Console.Writes.Count;

        // Forward replays the answered decisions step instead of asking it again. The decision
        // profile list filters as text is typed, so the letter verb would be a filter here: Right
        // is the forward verb at every picker.
        host.Console.Send(Key(KeyKind.Right));
        await host.WaitForFrameAsync("Write this configuration?");

        string since = Flatten(string.Concat(host.Console.Writes.Skip(before)));
        Assert.Contains("keeping decisions", since, StringComparison.Ordinal);
        Assert.DoesNotContain("Decisions settings", since, StringComparison.Ordinal);

        host.Console.SendEscape(); // nothing is written
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.Complete();
        await run;
    }

    [PosixFact]
    public async Task Escape_in_the_decisions_menu_returns_to_the_step_that_opened_it()
    {
        using var host = NavigationHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await host.ChooseDetailedModeAsync();
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Credential for openai");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("API key for openai");
        host.Console.SendText(Credential);
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Connection test (one discovery request");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Model (type to search");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Model check");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Profile name");
        host.Console.SendText("chat-main");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("fast usage");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Add another provider?");
        host.Console.SendEnter(); // Done
        await host.WaitForFrameAsync("Decision profile");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Decisions settings");

        // Esc is menu-local: the walk returns to the decision-profile step instead of ending.
        host.Console.SendEscape();
        await host.WaitForFrameAsync("Decision profile");
        Assert.DoesNotContain("setup cancelled", host.Frames, StringComparison.Ordinal);

        host.Console.SendEscape(); // a question's Esc ends the run
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.Complete();
        await run;
        Assert.False(File.Exists(SettingsPath(host)), "nothing is written on an abort");
    }

    [PosixFact]
    public async Task A_three_item_multi_select_shows_its_count_and_writes_the_chosen_profiles()
    {
        using var host = RoutingHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await WalkToDecisionsAsync(host);
        await PickRowAsync(host, "routing (decisions.routing)");
        await host.WaitForFrameAsync("Routing (decisions.routing)");
        await AnswerSwitchAsync(host, "> shadow");

        // Three candidates: the walk's chat profile and the settings file's two routing profiles.
        await host.WaitForFrameAsync("Routing profiles");
        host.Advance(500);
        host.Console.Send(Key(KeyKind.Space)); // chat-main
        await host.WaitForFrameAsync("[x] chat-main");
        host.Console.Send(Down());
        host.Advance(500);
        host.Console.Send(Key(KeyKind.Space)); // ops
        await host.WaitForFrameAsync("2 selected");
        Assert.Contains("[ ] nightly", host.Frames, StringComparison.Ordinal);
        host.Advance(500);
        host.Console.SendEnter();

        // One description per confirmed profile, then the menu returns with the routing row's value.
        // Each round waits for its own profile's prompt, never the generic prefix: the previous
        // prompt's frame still carries that prefix, and an answer sent against it lands in the
        // follow-up queue instead of the prompt that has not opened yet (a loaded runner paints the
        // next prompt after the key pump already accepted the text).
        foreach (
            (string profile, string description) in new[]
            {
                ("chat-main", "everyday work"),
                ("ops", "audits"),
            }
        )
        {
            await host.WaitForFrameAsync($"What is profile '{profile}'");
            host.Advance(500);
            host.Console.SendText(description);
            host.Advance(500);
            host.Console.SendEnter();
        }

        await host.WaitForFrameAsync("routing (decisions.routing) — shadow — chat-main, ops");

        // Done leaves the menu; Up twice from the routing row reaches it.
        host.Console.Send(Key(KeyKind.Up));
        host.Console.Send(Key(KeyKind.Up));
        await host.WaitForFrameAsync("> Done");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Write this configuration?");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("setup written");

        host.Console.Complete();
        await run;

        using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(SettingsPath(host)));
        JsonElement decisions = settings.RootElement.GetProperty("decisions");
        Assert.Equal("shadow", decisions.GetProperty("routing").GetString());
        Assert.Equal(
            ["chat-main", "ops"],
            decisions
                .GetProperty("routingProfiles")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray()
        );
        Assert.Equal(
            "everyday work",
            settings
                .RootElement.GetProperty("profiles")
                .GetProperty("chat-main")
                .GetProperty("description")
                .GetString()
        );
    }

    /// <summary>A walk with discovery data and no reasoning style, so the profile step asks once.</summary>
    private static InteractiveSessionHost NavigationHost() =>
        new(
            interactiveConsole: true,
            freshInstall: true,
            discoveryTransport: new RecordingHandler(
                (_, _) => Task.FromResult(Response(200, DiscoveryBody))
            )
        );

    /// <summary>A fresh walk whose settings file already names two routing profiles.</summary>
    private static InteractiveSessionHost RoutingHost()
    {
        var host = NavigationHost();
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "profiles": { "ops": { "provider": "openai", "model": "gpt-4.1" }, "nightly": { "provider": "openai", "model": "gpt-4.1" } },
              "decisions": { "routingProfiles": ["ops", "nightly"] }
            }
            """
        );
        return host;
    }

    private static async Task WalkToSummaryAsync(InteractiveSessionHost host)
    {
        await host.ChooseDetailedModeAsync();
        host.Console.SendEnter(); // openai

        await host.WaitForFrameAsync("Credential for openai");
        host.Console.SendEnter(); // the typed-key row
        await host.WaitForFrameAsync("API key for openai");
        host.Console.SendText(Credential);
        host.Console.SendEnter();

        await host.WaitForFrameAsync("Connection test (one discovery request");
        host.Console.SendEnter(); // run it

        await host.WaitForFrameAsync("Model (type to search");
        host.Console.SendEnter(); // the listed model

        await host.WaitForFrameAsync("Model check");
        host.Console.SendEnter(); // skip

        await host.WaitForFrameAsync("Profile name");
        host.Console.SendText("chat-main");
        host.Console.SendEnter();

        await host.WaitForFrameAsync("fast usage");
        host.Console.SendEnter(); // skip

        await host.WaitForFrameAsync("Add another provider?");
        host.Console.SendEnter(); // Done

        await host.WaitForFrameAsync("Decision profile");
        host.Console.SendEnter(); // skip

        await host.WaitForFrameAsync("Decisions settings");
        host.Console.SendEnter(); // Done: the defaults write nothing for the layer
    }

    /// <summary>The same walk, stopped on the decisions menu.</summary>
    private static async Task WalkToDecisionsAsync(InteractiveSessionHost host)
    {
        await host.ChooseDetailedModeAsync();
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Credential for openai");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("API key for openai");
        host.Console.SendText(Credential);
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Connection test (one discovery request");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Model (type to search");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Model check");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Profile name");
        host.Console.SendText("chat-main");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("fast usage");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Add another provider?");
        host.Console.SendEnter(); // Done
        await host.WaitForFrameAsync("Decision profile");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Decisions settings");
    }

    /// <summary>Moves the menu/picker cursor to the row and confirms it.</summary>
    private static async Task PickRowAsync(InteractiveSessionHost host, string row)
    {
        string previous = host.LastFrame;
        while (!host.LastFrame.Contains($"> {row}", StringComparison.Ordinal))
        {
            host.Console.Send(Down());
            await host.WaitUntilAsync(() =>
            {
                host.Advance(33);
                return host.LastFrame != previous;
            });
            previous = host.LastFrame;
        }

        host.Console.SendEnter();
    }

    /// <summary>Confirms a switch question on the row named by <paramref name="expectedRow"/>.</summary>
    private static async Task AnswerSwitchAsync(InteractiveSessionHost host, string expectedRow)
    {
        host.Advance(500); // the question's first 300 ms are discarded
        while (!host.LastFrame.Contains(expectedRow, StringComparison.Ordinal))
        {
            host.Console.Send(Down());
            await host.WaitForFrameAsync(expectedRow);
        }

        host.Advance(500);
        host.Console.SendEnter();
    }
}

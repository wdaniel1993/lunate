using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionSettingsTests
{
    [Fact]
    public async Task Settings_shows_every_resolved_setting_aligned_with_its_file_key()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" }
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/settings");
        host.Console.SendEnter();
        string scrollback = await InteractiveSessionCommandTests.WaitForScrollbackAsync(
            host,
            "subagents               4"
        );

        Assert.Contains("settings: ", scrollback);
        Assert.Contains(
            "model                   gpt-4o-mini",
            scrollback,
            StringComparison.Ordinal
        );
        Assert.Contains("approval                ask", scrollback, StringComparison.Ordinal);
        Assert.Contains("thinking                off", scrollback, StringComparison.Ordinal);
        Assert.Contains("thinkingFull            off", scrollback, StringComparison.Ordinal);
        Assert.Contains("toolDetails             collapsed", scrollback, StringComparison.Ordinal);
        Assert.Contains(
            "toolResultLimit         30,000 characters",
            scrollback,
            StringComparison.Ordinal
        );
        Assert.Contains("projection.batchTurns   4", scrollback, StringComparison.Ordinal);
        Assert.Contains("tui.theme               default", scrollback, StringComparison.Ordinal);
        Assert.Contains("tui.keys                none", scrollback, StringComparison.Ordinal);
        Assert.Contains("startupHeader           on", scrollback, StringComparison.Ordinal);
        Assert.Contains("updateCheck             on", scrollback, StringComparison.Ordinal);
        Assert.Contains("favorites               none", scrollback, StringComparison.Ordinal);
        Assert.Contains("tools.extra             none", scrollback, StringComparison.Ordinal);
        Assert.Contains("providers               none", scrollback, StringComparison.Ordinal);
        Assert.Contains("decisions.profile       none", scrollback, StringComparison.Ordinal);
        Assert.Contains("decisions.approvals     off", scrollback, StringComparison.Ordinal);
        Assert.Contains("decisions.approvalsThreshold 0.5", scrollback, StringComparison.Ordinal);
        Assert.Contains("decisions.routing       off", scrollback, StringComparison.Ordinal);
        Assert.Contains("decisions.routingProfiles none", scrollback, StringComparison.Ordinal);
        Assert.Contains("decisions.tool          off", scrollback, StringComparison.Ordinal);
        Assert.Contains("decisions.questions     off", scrollback, StringComparison.Ordinal);

        Assert.Contains(
            "roslyn                  max 2 loaded Roslyn workspaces",
            scrollback,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "subagents               4 concurrent",
            scrollback,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "editable here: approval, thinkingFull, toolDetails, toolResultLimit,",
            scrollback,
            StringComparison.Ordinal
        );
        Assert.Contains("tui.theme,", scrollback, StringComparison.Ordinal);
        Assert.Contains("tui.keys", scrollback, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Settings_applies_an_editable_key_to_this_session()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/settings toolDetails expanded");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "toolDetails: expanded (this session)"
        );

        host.Console.SendText("/settings");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(
            host,
            "toolDetails             expanded"
        );

        host.Console.SendText("/settings toolResultLimit 6000");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "toolResultLimit: 6,000 characters (this session)"
        );

        host.Console.SendText("/settings");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(
            host,
            "toolResultLimit         6,000"
        );

        host.Console.SendText("/settings approval auto-edit");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "approval: auto-edit (this session)"
        );

        host.Console.SendText("/settings");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(
            host,
            "approval                auto-edit"
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Bad_setting_arguments_are_refused_with_what_to_do_next()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/settings banana squish");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "settings: no such key 'banana'"
        );

        host.Console.SendText("/settings approval banana");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "approval must be 'ask' or 'auto-edit'"
        );

        host.Console.SendText("/settings toolResultLimit -3");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "toolResultLimit must be a positive integer"
        );

        host.Console.SendText("/settings model gpt-4o");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "the model has its own command — /model"
        );

        host.Console.SendText("/settings favorites on");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "favorites has its own commands — /favorite"
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}

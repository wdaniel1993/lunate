using Lunate.Extensibility;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The command-source seam through the real session: an installed extension command is composed
/// into the one registry (so autocomplete and dispatch see it), a declared command with no source
/// reads <c>declared, not provided</c> and cannot run, a built-in collision refuses the load, and a
/// throwing extension handler is a notice plus a log entry, never a crash.
/// </summary>
public sealed class CommandSourceTests
{
    [Fact]
    public async Task An_extension_command_is_composed_into_autocomplete_and_dispatch()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        CommandFixture.Install(store, settings: CommandFixture.Echo());
        var log = new RecordingExtensionLog();
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = log }
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/pi");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "/pi");
        AutocompleteState state = Assert.IsType<AutocompleteState>(host.Session.Autocomplete);
        Assert.Contains(
            state.Items,
            item => item.Text == "/ping" && item.Kind == CompletionKind.Extension
        );

        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);
        await host.SendLineAsync("/ping hello");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "ping commands: hello");

        Assert.Empty(host.Client.Requests);
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("command ping invoked with 'hello'", StringComparison.Ordinal)
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_declared_but_unprovided_command_is_not_runnable_and_reads_declared_not_provided()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        CommandFixture.Install(store, commands: """["ghost"]""");
        var log = new RecordingExtensionLog();
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = log }
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "declared, not provided: ghost"
        );

        host.Console.SendText("/ghost");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "unknown command: /ghost");

        host.Console.SendText("/g");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "/g");
        Assert.DoesNotContain(
            host.Session.Autocomplete?.Items ?? [],
            item => item.Text == "/ghost"
        );
        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);

        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("declares command '/ghost'", StringComparison.Ordinal)
                && message.Contains("not runnable", StringComparison.Ordinal)
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_built_in_name_collision_refuses_the_load_naming_both()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        CommandFixture.Install(
            store,
            commands: """["help"]""",
            settings: CommandFixture.Echo("help", "Extension help")
        );
        var log = new RecordingExtensionLog();
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = log }
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "failed");

        Assert.Contains(
            "collides with the built-in command '/help'",
            host.Frames,
            StringComparison.Ordinal
        );
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("extension 'commands'", StringComparison.Ordinal)
                && message.Contains("'/help'", StringComparison.Ordinal)
                && message.Contains("built-in", StringComparison.Ordinal)
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_throwing_extension_handler_is_a_notice_and_a_log_entry()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        CommandFixture.Install(
            store,
            commands: """["boom"]""",
            settings: CommandFixture.Throwing("boom")
        );
        var log = new RecordingExtensionLog();
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = log }
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await host.SendLineAsync("/boom");
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "command /boom failed: the scripted command exploded"
        );
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("command '/boom'", StringComparison.Ordinal)
                && message.Contains("source 'commands'", StringComparison.Ordinal)
                && message.Contains("scripted command exploded", StringComparison.Ordinal)
        );

        // The session is still alive and later commands still dispatch.
        host.Console.SendText("/help");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "/compact");
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}

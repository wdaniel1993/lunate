using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// Help lists live entries only, and a shadowed command stays legible where its owner is named
/// (sign-off Q3/Q5): two extensions offering <c>ping</c> compose to one live entry, <c>/help</c>
/// shows it once, and <c>/extensions</c> reports the loser as <c>shadowed by ext:alpha:ping</c>.
/// </summary>
public sealed class CommandShadowTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_shadowed_command_reads_shadowed_by_the_owner_id_in_the_extensions_view()
    {
        using var temp = new TempDirectory();
        (ExtensionSession host, CommandRegistry registry) = await ComposeAsync(temp);

        Assert.Equal(["ping"], registry.Descriptors.Select(descriptor => descriptor.Name));
        Assert.Equal("ext:alpha:ping", Assert.Single(registry.Entries).Id);
        Assert.Equal("ext:beta:ping", Assert.Single(registry.Shadowed).ShadowedId);

        Assert.Empty(host.RegisteredCounts("alpha", registry.Shadowed).ShadowedCommandOwners);
        Assert.Equal(
            ["ext:alpha:ping"],
            host.RegisteredCounts("beta", registry.Shadowed).ShadowedCommandOwners
        );
        string beta = Assert.Single(
            ExtensionsView.Segments(
                host.Descriptors,
                host.LoadResults,
                id => host.RegisteredCounts(id, registry.Shadowed)
            ),
            segment => segment.StartsWith("beta ", StringComparison.Ordinal)
        );
        Assert.Contains(
            "commands 1 (1 declared, shadowed by ext:alpha:ping)",
            beta,
            StringComparison.Ordinal
        );

        // Help lists the live entry only — the shadowed one is not a live command.
        Assert.Single(
            HelpView.Lines(registry.Descriptors),
            line => line.Contains("/ping", StringComparison.Ordinal)
        );
        await host.DisposeAsync();
    }

    [Fact]
    public async Task The_session_extensions_notice_names_the_shadowing_id()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        Install(store);
        var log = new RecordingExtensionLog();
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = log }
        );
        // Wide enough that the one-line /extensions notice keeps the shadowed entry visible.
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await host.SendLineAsync("/extensions");
        await host.WaitForFrameAsync("shadowed by ext:alpha:ping");

        await host.SendLineAsync("/help");
        // The count needs the whole listing, so the wait's snapshot is the subject of the assert.
        string scrollback = await host.WaitForScrollbackAsync("  /ping — Ping the server");
        Assert.Equal(
            1,
            scrollback
                .Split('\n')
                .Count(line => line.Contains("/ping — Ping the server", StringComparison.Ordinal))
        );
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("ext:beta:ping", StringComparison.Ordinal)
                && message.Contains("ext:alpha:ping", StringComparison.Ordinal)
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static async Task<(ExtensionSession Host, CommandRegistry Registry)> ComposeAsync(
        TempDirectory temp
    )
    {
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        Install(store);
        ExtensionSession host = await ExtensionSession.StartAsync(
            new WorkspaceInfo(Workspace.Canonicalize(worktree), null, null),
            new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            Ct
        );
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [.. host.CommandSources()],
            host.Log,
            Ct
        );
        return (host, registry);
    }

    private static void Install(string store)
    {
        CommandFixture.Install(
            store,
            id: "alpha",
            settings: CommandFixture.Echo("ping", "Ping the server")
        );
        CommandFixture.Install(
            store,
            id: "beta",
            settings: CommandFixture.Echo("ping", "Ping the server too")
        );
    }
}

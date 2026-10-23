using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The invocation lifetime through the real session host (design D3, sign-off Q2/Q3): unloading an
/// extension while its command runs cancels it, tells the user with a notice naming the extension
/// and the command id, and leaves the rest of the registry working; a session quit cancels in
/// flight commands silently, because no user is looking at the session any more.
/// </summary>
public sealed class CommandUnloadTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unloading_an_extension_mid_command_notices_and_later_commands_still_work()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        CommandFixture.Install(
            store,
            id: "alpha",
            settings: """{"commands":[{"name":"ping","description":"Ping it","behavior":"block"}]}"""
        );
        CommandFixture.Install(
            store,
            id: "beta",
            commands: """["pong"]""",
            settings: CommandFixture.Echo("pong", "Pong it")
        );
        var log = new RecordingExtensionLog();
        var notices = new List<string>();
        await using ExtensionSession host = await ExtensionSession.StartAsync(
            new WorkspaceInfo(Workspace.Canonicalize(worktree), null, null),
            new ExtensionSeams
            {
                StorePath = store,
                Log = log,
                Notice = notices.Add,
            },
            Ct
        );
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [.. host.CommandSources()],
            log,
            Ct
        );
        Assert.Equal(["ping", "pong"], registry.Descriptors.Select(descriptor => descriptor.Name));

        Task<CommandResult> invocation = Task.Run(async () =>
            await registry.InvokeAsync(new CommandInvocation("ping", "now"), Ct)
        );
        await WaitUntilAsync(() => log.Contains("command ping invoked with 'now'"));

        await host.UnloadAsync("alpha").AsTask().WaitAsync(TimeSpan.FromSeconds(10), Ct);

        CommandResult cancelled = await invocation.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Null(cancelled.Message);
        Assert.Contains(
            notices,
            notice =>
                notice.Contains("command ext:alpha:ping", StringComparison.Ordinal)
                && notice.Contains("extension 'alpha'", StringComparison.Ordinal)
        );

        // The rest of the registry keeps dispatching, and a recomposition no longer offers the
        // unloaded extension's command.
        CommandResult pong = await registry.InvokeAsync(new CommandInvocation("pong", "again"), Ct);
        Assert.Equal("pong beta: again", pong.Message);
        CommandRegistry recomposed = await CommandRegistry.ComposeAsync(
            [.. host.CommandSources()],
            log,
            Ct
        );
        Assert.Equal(["pong"], recomposed.Descriptors.Select(descriptor => descriptor.Name));
    }

    [Fact]
    public async Task A_session_quit_cancels_in_flight_commands_silently()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        CommandFixture.Install(
            store,
            id: "alpha",
            settings: """{"commands":[{"name":"ping","description":"Ping it","behavior":"block"}]}"""
        );
        var log = new RecordingExtensionLog();
        var notices = new List<string>();
        ExtensionSession host = await ExtensionSession.StartAsync(
            new WorkspaceInfo(Workspace.Canonicalize(worktree), null, null),
            new ExtensionSeams
            {
                StorePath = store,
                Log = log,
                Notice = notices.Add,
            },
            Ct
        );
        ICommandSource source = Assert.Single(host.CommandSources());
        Task<CommandResult> invocation = Task.Run(async () =>
            await source.InvokeAsync(new CommandInvocation("ping", "quit"), Ct)
        );
        await WaitUntilAsync(() => log.Contains("command ping invoked with 'quit'"));

        // The quit path: the TUI is torn down, then the host stops.
        await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), Ct);

        CommandResult result = await invocation.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Null(result.Message);
        Assert.Empty(notices);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("the condition never became true");
            }

            await Task.Delay(5, Ct);
        }
    }
}

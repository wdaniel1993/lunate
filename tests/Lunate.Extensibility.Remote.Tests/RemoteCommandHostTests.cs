using System.Diagnostics;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Testing;

namespace Lunate.Extensibility.Remote.Tests;

/// <summary>
/// A remote manifest command through the real loader and registry plumbing: the capability the
/// child advertises decides whether its declared commands are offered; a provided command
/// dispatches over the wire under the same lease an in-process one takes, so an unload cancels it,
/// names it in one notice and drains within the hook-handler bound; and a failing handler raises
/// the message the command registry turns into the in-process error-notice shape.
/// </summary>
public sealed class RemoteCommandHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_manifest_command_dispatches_over_the_wire_and_its_output_returns()
    {
        using var workspace = new RemoteTestWorkspace(commands: ["ping"]);
        ExtensionLoader loader = workspace.CreateLoader();
        await workspace.LoadAsync(loader);

        RegisteredCommandSource registered = Assert.Single(loader.CommandSources.List());
        CommandDescriptor descriptor = Assert.Single(registered.Descriptors);
        Assert.Equal("ping", descriptor.Name);
        Assert.Equal(CommandKind.Extension, descriptor.Kind);
        Assert.Equal("remote-sample", descriptor.SourceId);
        Assert.Equal(RemoteExtensionState.Ready, loader.RemoteStatus("remote-sample")!.State);

        CommandResult result = await registered.Source.InvokeAsync(
            new CommandInvocation("ping", "hello world"),
            Ct
        );

        Assert.False(result.IsError);
        Assert.Equal("ext:remote-sample:ping: pong hello world", result.Message);
        CommandInvokeRequest request = Assert.Single(workspace.Child.CommandRequests);
        Assert.Equal("ping", request.Name);
        Assert.Equal("hello world", request.Argument);
        Assert.True(request.RequestId > 0);
    }

    [Fact]
    public async Task A_failing_remote_handler_raises_the_message_the_registry_shapes()
    {
        using var workspace = new RemoteTestWorkspace(commands: ["explode"]);
        workspace.Child.CommandAnswers = (_, _) =>
            throw new InvalidOperationException("the extension broke while pinging");
        ExtensionLoader loader = workspace.CreateLoader();
        await workspace.LoadAsync(loader);
        ICommandSource source = Assert.Single(loader.CommandSources.List()).Source;

        RemoteExtensionException exception = await Assert.ThrowsAsync<RemoteExtensionException>(
            () =>
                source.InvokeAsync(new CommandInvocation("explode", string.Empty), Ct).AsTask()
        );

        // The registry wraps this message as "command ext:<id>:<name> failed: <message> — see
        // /extensions for the extension's state", the same shape an in-process handler produces.
        Assert.Equal("the extension broke while pinging", exception.Message);
    }

    [Fact]
    public async Task Unloading_during_an_invocation_cancels_it_notifies_and_completes_within_the_bound()
    {
        using var workspace = new RemoteTestWorkspace(commands: ["wait"]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Child.CommandAnswers = async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return CommandResult.None;
        };
        ExtensionLoader loader = workspace.CreateLoader();
        await workspace.LoadAsync(loader);
        ICommandSource source = Assert.Single(loader.CommandSources.List()).Source;

        Task<CommandResult> invocation = Task.Run(() =>
            source.InvokeAsync(new CommandInvocation("wait", string.Empty), Ct).AsTask()
        );
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        long timestamp = Stopwatch.GetTimestamp();
        await loader.Unload("remote-sample").AsTask().WaitAsync(TimeSpan.FromSeconds(10), Ct);
        TimeSpan unload = Stopwatch.GetElapsedTime(timestamp);

        CommandResult cancelled = await invocation.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Null(cancelled.Message);
        Assert.Contains(
            workspace.Notices,
            notice =>
                notice.Contains("command ext:remote-sample:wait", StringComparison.Ordinal)
                && notice.Contains("extension 'remote-sample'", StringComparison.Ordinal)
        );
        // The drain is bounded by the hook-handler timeout (five seconds by default); a cancelled
        // invocation returns far sooner.
        Assert.True(
            unload < TimeSpan.FromSeconds(5),
            $"the unload took {unload.TotalMilliseconds:0} ms"
        );
        Assert.Empty(loader.CommandSources.List());
    }

    [Fact]
    public async Task A_refused_manifest_stops_the_child_the_listing_started()
    {
        using var workspace = new RemoteTestWorkspace(commands: ["help"]);
        ExtensionLoader loader = workspace.CreateLoader(reservedCommands: ["help"]);

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            workspace.LoadAsync(loader)
        );

        Assert.Contains("collides with the built-in", exception.Message, StringComparison.Ordinal);
        Assert.Empty(loader.CommandSources.List());
        // The child was started to learn the capability; a refused load must not leave it running.
        Assert.True(workspace.Child.HasExited);
    }

    [Fact]
    public async Task A_refused_manifest_leaves_no_proxy_registration_behind()
    {
        // A declared surface wide enough that every proxy kind registers before the command
        // cross-check refuses the manifest: hooks, a service and a file-change subscription.
        using var workspace = new RemoteTestWorkspace(
            hooks: ["tool-calling", "file-changed"],
            commands: ["help"],
            services: ["greeter"]
        );
        ExtensionLoader loader = workspace.CreateLoader(reservedCommands: ["help"]);
        using var bus = new FileChangeBus(workspace.Worktree, workspace.Log);
        loader.Services.RegisterCore("core/file-bus", bus);

        await Assert.ThrowsAsync<ExtensionLoadException>(() => workspace.LoadAsync(loader));

        Assert.Equal(0, loader.Hooks.Count("remote-sample"));
        Assert.Equal(0, loader.Services.Count("remote-sample"));
        Assert.Empty(loader.CommandSources.List());
        Assert.Empty(loader.Tools.List());
        Assert.Null(loader.RemoteStatus("remote-sample"));
        Assert.Null(loader.RemoteStartDuration("remote-sample"));

        // The file-change subscription is gone with them: a published change reaches nothing.
        int notifications = workspace.Child.Notifications.Count;
        bus.Notify(
            new FileChangeEvent("/work/a.cs", "a.cs", "/work", null, FileChangeKind.Modified)
        );
        await bus.DrainAsync(Ct);
        Assert.Equal(notifications, workspace.Child.Notifications.Count);
    }

    [Fact]
    public async Task A_version_2_child_that_does_not_advertise_commands_is_not_offered_them()
    {
        using var workspace = new RemoteTestWorkspace(commands: ["ghost"]);
        workspace.Child.AdvertisesCommands = false;
        ExtensionLoader loader = workspace.CreateLoader();
        await workspace.LoadAsync(loader);

        // Version 2 alone is not a statement of intent: the capability is, so a fork that speaks
        // the version and omits the flag gets the same listing as a version-1 child.
        Assert.Equal(RemoteProtocol.Version, workspace.Child.LastHandshake!.ProtocolVersion);
        Assert.Equal(RemoteExtensionState.Ready, loader.RemoteStatus("remote-sample")!.State);
        Assert.Empty(loader.CommandSources.List().SelectMany(registered => registered.Descriptors));
        Assert.Equal(["ghost"], loader.DeclaredNotProvidedCommands("remote-sample"));
    }

    [Fact]
    public async Task A_child_without_the_capability_is_not_offered_its_declared_commands()
    {
        using var workspace = new RemoteTestWorkspace(commands: ["ghost"]);
        workspace.Child.AdvertisesCommands = false;
        workspace.Child.Handshake = request => new InitializeResult(
            RemoteProtocol.MinimumVersion,
            request.Hooks,
            request.Services
        );
        ExtensionLoader loader = workspace.CreateLoader();
        await workspace.LoadAsync(loader);

        // Version 1 stays readable for hooks and services — and its declared command is reported
        // as declared, not provided rather than offered and dead.
        Assert.Equal(RemoteExtensionState.Ready, loader.RemoteStatus("remote-sample")!.State);
        Assert.Empty(loader.CommandSources.List().SelectMany(registered => registered.Descriptors));
        Assert.Equal(["ghost"], loader.DeclaredNotProvidedCommands("remote-sample"));
        Assert.Contains(
            workspace.Log.Warnings,
            warning =>
                warning.Contains("/ghost", StringComparison.Ordinal)
                && warning.Contains("not runnable", StringComparison.Ordinal)
        );
    }
}

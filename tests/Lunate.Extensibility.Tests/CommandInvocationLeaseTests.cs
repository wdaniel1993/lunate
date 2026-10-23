using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// An extension command invocation holds a lease on its extension (design D3, sign-off Q2):
/// unloading the extension cancels the live invocation's token, tells the user with a notice naming
/// the extension and the command id, waits a bounded time for the handler to leave, and only then
/// unloads the context. A handler that swallows its token is logged once and does not hold the
/// unload open; an idle extension unloads without touching the lease; a late result is discarded;
/// and a host call after the drain throws <see cref="ObjectDisposedException"/>, contained and
/// logged with the command id.
/// </summary>
public sealed class CommandInvocationLeaseTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unload_cancels_an_in_flight_command_and_returns_after_it_leaves()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallCommandExtension(
            temp,
            "cmd",
            """["ping"]""",
            """{"commands":[{"name":"ping","description":"Ping it","behavior":"block"}]}"""
        );
        var log = new RecordingExtensionLog();
        var notices = new List<string>();
        ExtensionLoader loader = Loader(temp, log, notices);
        loader.Discover(temp.Root, "repo");
        await loader.Load("cmd", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);
        ICommandSource source = Assert.Single(loader.CommandSources.List()).Source;
        Task<CommandResult> invocation = Task.Run(async () =>
            await source.InvokeAsync(new CommandInvocation("ping", "hello"), Ct)
        );
        await WaitForLogAsync(log, "command ping invoked with 'hello'");

        await loader.Unload("cmd").AsTask().WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // The cancelled handler's result is discarded, never rendered (sign-off Q2).
        CommandResult result = await invocation.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Null(result.Message);
        Assert.Contains(
            notices,
            notice =>
                notice.Contains("command ext:cmd:ping", StringComparison.Ordinal)
                && notice.Contains("extension 'cmd'", StringComparison.Ordinal)
                && notice.Contains("was unloaded", StringComparison.Ordinal)
        );
        Assert.Empty(loader.CommandSources.List());
    }

    [Fact]
    public async Task A_handler_that_swallows_its_token_is_logged_once_and_does_not_hold_the_unload()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallCommandExtension(
            temp,
            "cmd",
            """["ping"]""",
            """{"commands":[{"name":"ping","description":"Ping it","behavior":"ignore","delayMs":1500}]}"""
        );
        var log = new RecordingExtensionLog();
        ExtensionLoader loader = Loader(
            temp,
            log,
            notices: null,
            new HookRunnerOptions { HandlerTimeout = TimeSpan.FromMilliseconds(150) }
        );
        loader.Discover(temp.Root, "repo");
        await loader.Load("cmd", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);
        ICommandSource source = Assert.Single(loader.CommandSources.List()).Source;
        Task<CommandResult> invocation = Task.Run(async () =>
            await source.InvokeAsync(new CommandInvocation("ping", "hello"), Ct)
        );
        await WaitForLogAsync(log, "command ping invoked with 'hello'");

        long elapsed = await TimeAsync(() =>
            loader.Unload("cmd").AsTask().WaitAsync(TimeSpan.FromSeconds(10), Ct)
        );

        // Bounded by the hook-handler timeout, not by the handler's own delay.
        Assert.InRange(elapsed, 100, 1200);
        Assert.Single(
            log.Messages,
            message =>
                message.Contains("command ext:cmd:ping", StringComparison.Ordinal)
                && message.Contains("did not return", StringComparison.Ordinal)
        );
        Assert.Empty(loader.CommandSources.List());

        // The handler finishes later; its result is discarded and nothing is rendered.
        CommandResult result = await invocation.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task An_idle_extension_unloads_without_touching_the_lease()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallCommandExtension(
            temp,
            "cmd",
            """["ping"]""",
            """{"commands":[{"name":"ping","description":"Ping it"}]}"""
        );
        var log = new RecordingExtensionLog();
        var notices = new List<string>();
        ExtensionLoader loader = Loader(temp, log, notices);
        loader.Discover(temp.Root, "repo");
        await loader.Load("cmd", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        long elapsed = await TimeAsync(() =>
            loader.Unload("cmd").AsTask().WaitAsync(TimeSpan.FromSeconds(10), Ct)
        );

        Assert.True(elapsed < 1500, $"an idle unload waited {elapsed} ms");
        Assert.Empty(notices);
        Assert.DoesNotContain(log.Messages, message => message.Contains("did not return"));
        Assert.Empty(loader.CommandSources.List());
    }

    [Fact]
    public async Task A_host_call_after_the_drain_throws_object_disposed_contained_and_logged()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallCommandExtension(
            temp,
            "cmd",
            """["ping"]""",
            """{"commands":[{"name":"ping","description":"Ping it","behavior":"late","delayMs":400}]}"""
        );
        var log = new RecordingExtensionLog();
        ExtensionLoader loader = Loader(
            temp,
            log,
            notices: null,
            new HookRunnerOptions { HandlerTimeout = TimeSpan.FromMilliseconds(150) }
        );
        loader.Discover(temp.Root, "repo");
        await loader.Load("cmd", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);
        RegisteredCommandSource registered = Assert.Single(loader.CommandSources.List());
        Task<CommandResult> invocation = Task.Run(async () =>
            await registered.Source.InvokeAsync(new CommandInvocation("ping", "hi"), Ct)
        );
        await WaitForLogAsync(log, "command ping invoked with 'hi'");

        await loader.Unload("cmd").AsTask().WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // The handler calls the host after the drain; the disposed context throws, the wrapper
        // contains it, logs it with the command id and renders nothing.
        CommandResult result = await invocation.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Null(result.Message);
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("command ext:cmd:ping", StringComparison.Ordinal)
                && message.Contains("ObjectDisposedException", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_stale_source_cannot_start_a_handler_after_the_unload()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallCommandExtension(
            temp,
            "cmd",
            """["ping"]""",
            """{"commands":[{"name":"ping","description":"Ping it"}]}"""
        );
        var log = new RecordingExtensionLog();
        ExtensionLoader loader = Loader(temp, log, notices: null);
        loader.Discover(temp.Root, "repo");
        await loader.Load("cmd", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);
        // The session's registry snapshot still holds this wrapper after the unload.
        ICommandSource source = Assert.Single(loader.CommandSources.List()).Source;
        await loader.Unload("cmd").AsTask().WaitAsync(TimeSpan.FromSeconds(10), Ct);

        CommandResult result = await source
            .InvokeAsync(new CommandInvocation("ping", "late"), Ct)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Null(result.Message);
        Assert.DoesNotContain(
            log.Messages,
            message =>
                message.Contains("command ping invoked with 'late'", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Set_descriptors_still_lands_on_the_registration_behind_the_wrapper()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallCommandExtension(
            temp,
            "cmd",
            """["ping"]""",
            """{"commands":[{"name":"ping","description":"Ping it","argumentHint":"<text>"}]}"""
        );
        var log = new RecordingExtensionLog();
        ExtensionLoader loader = Loader(temp, log, notices: null);
        loader.Discover(temp.Root, "repo");

        await loader.Load("cmd", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        // The load-time stamping must reach the registration even though Register wrapped it.
        CommandDescriptor descriptor = Assert.Single(
            Assert.Single(loader.CommandSources.List()).Descriptors
        );
        Assert.Equal("ping", descriptor.Name);
        Assert.Equal("Ping it", descriptor.Description);
        Assert.Equal("<text>", descriptor.ArgumentHint);
        Assert.Equal("cmd", descriptor.SourceId);
        Assert.Equal(CommandKind.Extension, descriptor.Kind);
    }

    private static ExtensionLoader Loader(
        TempDirectory temp,
        RecordingExtensionLog log,
        List<string>? notices,
        HookRunnerOptions? hookOptions = null
    ) =>
        new(
            new ExtensionHostOptions
            {
                StorePath = temp.Subdirectory("store"),
                Log = log,
                Notify = notices is null ? null : notices.Add,
            },
            hookOptions is null ? null : new HookRunner(hookOptions, log)
        );

    private static async Task WaitForLogAsync(RecordingExtensionLog log, string text)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!log.Contains(text))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"the handler never logged '{text}'; log: {string.Join(" | ", log.Messages)}"
                );
            }

            await Task.Delay(5, Ct);
        }
    }

    private static async Task<long> TimeAsync(Func<Task> action)
    {
        long started = Environment.TickCount64;
        await action();
        return Environment.TickCount64 - started;
    }
}

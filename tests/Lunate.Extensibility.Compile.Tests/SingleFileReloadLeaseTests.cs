using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>
/// Task 3.5: a reload rides the existing command-lease path, and <c>ToolsChanged</c> fires only after
/// the new build is loaded. A command that is executing when the reload lands finishes against the
/// old build's context (its result is kept, not discarded, because nothing cancelled it), the old
/// lease is gone before its context is, and the notification the harness refreshes on carries the new
/// build's tool set — never a half-swapped one.
/// </summary>
public sealed class SingleFileReloadLeaseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_command_that_runs_during_the_build_finishes_on_the_old_build_and_the_swap_notifies_once()
    {
        using SingleFileLoadFixture fixture = SingleFileLoadFixture.Create();
        fixture.AddToolPackage(SingleFileLoadFixture.Lib, Ct);
        SingleFileBuildOutcome first = await fixture.BuildAsync(
            SingleFileLoadFixture.Source(probe: "\"first\""),
            cancellationToken: Ct
        );
        ExtensionLoader loader = fixture.Discovered();
        var observer = new ToolsChangedObserver(loader, fixture.Id);
        loader.Hooks.Register("reload-observer", observer);
        await loader.LoadSingleFileAsync(fixture.Id, Ct);
        observer.Events.Clear();

        // Park the reload inside its build, so the command below runs while the reload is in flight
        // but before it has touched anything.
        fixture.Engine.Hold();
        fixture.Write(SingleFileLoadFixture.Source(probe: "\"second\""));
        // On its own task: the gated engine blocks the caller while it waits, and the test thread
        // has a command to run before the gate opens.
        Task<SingleFileReloadOutcome> reload = Task.Run(async () =>
            await loader.ReloadSingleFileAsync(
                fixture.Id,
                fixture.Builder(),
                new ApprovingPrompt(),
                Ct
            )
        );
        await fixture.Engine.WaitEnteredAsync(TimeSpan.FromSeconds(10), Ct);

        // A command the session starts now is leased on the old build and answers from it.
        CommandResult duringBuild = await CommandAsync(loader, "ping");
        Assert.Equal("first", duringBuild.Message);
        Assert.Equal(first.Build!.Key, loader.LoadedSingleFileKey(fixture.Id));

        fixture.Engine.Release();
        SingleFileReloadOutcome outcome = await reload.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.True(outcome.Reloaded, outcome.Reason);
        // Nothing was in flight when the swap cancelled, so nothing was cancelled or discarded.
        Assert.Empty(outcome.CancelledCommands);
        Assert.NotEqual(first.Build.Key, outcome.LoadedBuildKey);

        // One notification, fired after the new build was loaded: the observer saw the new key and a
        // registered tool for the extension at the moment of the dispatch.
        ToolsChangedEvent dispatched = Assert.Single(observer.Events);
        Assert.Equal(outcome.LoadedBuildKey, dispatched.LoadedKey);
        Assert.Contains("acme-tools__whoami", dispatched.Tools);
        Assert.True(dispatched.ExtensionToolRegistered);

        // And the next call sees the new build.
        Assert.Equal("second", (await CommandAsync(loader, "ping")).Message);
    }

    private static async Task<CommandResult> CommandAsync(ExtensionLoader loader, string name)
    {
        ICommandSource source = Assert.Single(loader.CommandSources.List()).Source;
        return await source
            .InvokeAsync(new CommandInvocation(name, string.Empty), Ct)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    /// <summary>What the loader looked like at the instant <c>ToolsChanged</c> was dispatched.</summary>
    private sealed record ToolsChangedEvent(
        IReadOnlyList<string> Tools,
        string? LoadedKey,
        bool ExtensionToolRegistered
    );

    /// <summary>
    /// A host handler on the same hook the harness listens to: it records the payload and the
    /// loader's own view at dispatch time, which is the only place "the list was swapped before the
    /// notification" can be observed.
    /// </summary>
    private sealed class ToolsChangedObserver(ExtensionLoader loader, string extensionId)
        : IToolsChangedHandler
    {
        internal List<ToolsChangedEvent> Events { get; } = [];

        public int Priority => 0;

        public ValueTask ObserveAsync(
            ToolsChangedPayload payload,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(
                new ToolsChangedEvent(
                    payload.Tools,
                    loader.LoadedSingleFileKey(extensionId),
                    loader
                        .Tools.List()
                        .Any(tool =>
                            string.Equals(tool.ExtensionId, extensionId, StringComparison.Ordinal)
                            && string.Equals(tool.LocalName, "whoami", StringComparison.Ordinal)
                        )
                )
            );
            return ValueTask.CompletedTask;
        }
    }
}

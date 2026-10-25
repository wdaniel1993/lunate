using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>
/// Task 3.4: a reload cancels the old build's live invocations with a notice that names the
/// extension, drains them within <see cref="ExtensionLoader.ReloadDrainBound"/>, and only then
/// disposes the context. A handler that ignores its token does not hold the swap open: it is logged
/// once, the reload completes, and its result is discarded.
/// </summary>
public sealed class SingleFileReloadDrainTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_handler_that_honours_its_token_is_cancelled_drained_and_never_rendered()
    {
        using SingleFileLoadFixture fixture = SingleFileLoadFixture.Create();
        fixture.AddToolPackage(SingleFileLoadFixture.Lib, Ct);
        await fixture.BuildAsync(
            SingleFileLoadFixture.Source(
                probe: "\"first\"",
                // Blocks until the reload cancels it, which is what the drain is for.
                commandBody: "await Task.Delay(Timeout.Infinite, cancellationToken); return CommandResult.Notice(Probe.Value);"
            ),
            cancellationToken: Ct
        );
        ExtensionLoader loader = fixture.Discovered();
        await loader.LoadSingleFileAsync(fixture.Id, Ct);
        Task<CommandResult> invocation = InvokeAsync(loader);
        await WaitForPing(fixture);

        SingleFileReloadOutcome outcome = await loader.ReloadSingleFileAsync(
            fixture.Id,
            fixture.Builder(),
            new ApprovingPrompt(),
            Ct
        );

        Assert.True(outcome.Reloaded, outcome.Reason);
        Assert.True(outcome.Drained);
        Assert.Equal(["ext:acme-tools:ping"], outcome.CancelledCommands);
        // The user is told once, by command and by extension.
        string notice = Assert.Single(
            fixture.Notices,
            text => text.Contains("was cancelled", StringComparison.Ordinal)
        );
        Assert.Contains("ext:acme-tools:ping", notice, StringComparison.Ordinal);
        Assert.Contains("extension 'acme-tools'", notice, StringComparison.Ordinal);
        // Drained before the context disposed: the invocation had already left when the swap ended,
        // and its cancelled result is discarded rather than rendered.
        Assert.True(invocation.IsCompleted);
        Assert.Null((await invocation).Message);
        Assert.DoesNotContain(
            fixture.Log.Messages,
            message => message.Contains("did not return", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_handler_that_ignores_its_token_is_logged_once_and_does_not_hold_the_reload()
    {
        // Derived from the bound, so a change to the bound moves the test with it: the handler
        // outlives the drain by a wide margin and the reload must not wait for it.
        int ignoreMs = (int)(ExtensionLoader.ReloadDrainBound.TotalMilliseconds * 4);
        using SingleFileLoadFixture fixture = SingleFileLoadFixture.Create();
        fixture.AddToolPackage(SingleFileLoadFixture.Lib, Ct);
        await fixture.BuildAsync(
            SingleFileLoadFixture.Source(
                probe: "\"first\"",
                commandBody: $"using var gate = new ManualResetEventSlim(false); gate.Wait(TimeSpan.FromMilliseconds({ignoreMs}), CancellationToken.None); return CommandResult.Notice(Probe.Value);"
            ),
            cancellationToken: Ct
        );
        ExtensionLoader loader = fixture.Discovered();
        await loader.LoadSingleFileAsync(fixture.Id, Ct);
        ICommandSource source = Assert.Single(loader.CommandSources.List()).Source;
        Task<CommandResult> invocation = Task.Run(async () =>
            await source.InvokeAsync(new CommandInvocation("ping", string.Empty), Ct)
        );
        await WaitForPing(fixture);

        long started = Environment.TickCount64;
        SingleFileReloadOutcome outcome = await loader
            .ReloadSingleFileAsync(fixture.Id, fixture.Builder(), new ApprovingPrompt(), Ct)
            .WaitAsync(TimeSpan.FromSeconds(30), Ct);
        long elapsed = Environment.TickCount64 - started;

        // The reload finished long before the handler would have; the bound is what makes that true.
        Assert.True(outcome.Reloaded, outcome.Reason);
        Assert.False(outcome.Drained);
        Assert.True(
            elapsed < ignoreMs,
            FormattableString.Invariant(
                $"the reload waited {elapsed} ms for a handler that ignores its token"
            )
        );
        // Exactly one line for the stuck handler, naming it and the extension, and the reload
        // carried on regardless.
        string line = Assert.Single(
            fixture.Log.Matching("did not return within"),
            message => message.Contains("ext:acme-tools:ping", StringComparison.Ordinal)
        );
        Assert.Contains("reload", line, StringComparison.Ordinal);
        Assert.Contains("acme-tools", line, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Log.Count("did not return within"));

        // The handler finishes later; its result is discarded, never rendered.
        Assert.Null((await invocation.WaitAsync(TimeSpan.FromSeconds(30), Ct)).Message);
    }

    private static Task<CommandResult> InvokeAsync(ExtensionLoader loader)
    {
        ICommandSource source = Assert.Single(loader.CommandSources.List()).Source;
        return Task.Run(async () =>
            await source.InvokeAsync(new CommandInvocation("ping", string.Empty), Ct)
        );
    }

    /// <summary>Waits until the command handler is running, so the reload meets a live invocation.</summary>
    private static async Task WaitForPing(SingleFileLoadFixture fixture)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!fixture.Log.Contains("ping invoked"))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    "the command never started; log: " + string.Join(" | ", fixture.Log.Messages)
                );
            }

            await Task.Delay(5, Ct);
        }
    }
}

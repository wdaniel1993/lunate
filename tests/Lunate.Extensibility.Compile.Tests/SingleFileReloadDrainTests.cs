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
        // The handler waits for a file this test writes when it is done, not for a fixed stretch:
        // a loaded runner can spend longer than any such stretch on fixture setup, and a handler
        // that has already returned proves nothing about a reload that must not wait for it.
        using SingleFileLoadFixture fixture = SingleFileLoadFixture.Create();
        string release = Path.Combine(fixture.Store, "release-stuck-handler");
        string releaseLiteral = release.Replace("\"", "\"\"", StringComparison.Ordinal);
        fixture.AddToolPackage(SingleFileLoadFixture.Lib, Ct);
        await fixture.BuildAsync(
            SingleFileLoadFixture.Source(
                probe: "\"first\"",
                commandBody: $"while (!System.IO.File.Exists(@\"{releaseLiteral}\")) {{ System.Threading.Thread.Sleep(25); }} return CommandResult.Notice(Probe.Value);"
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

        CommandResult? finished = null;
        try
        {
            SingleFileReloadOutcome outcome = await loader
                .ReloadSingleFileAsync(fixture.Id, fixture.Builder(), new ApprovingPrompt(), Ct)
                .WaitAsync(TimeSpan.FromSeconds(30), Ct);

            // The reload finished while the handler was still in it — the handler cannot have returned,
            // because only this test releases it, and it has not yet. No wall-clock assertion here: the
            // reload's total also carries the new build's restore and compile, which is not this test's
            // subject and grows on a loaded runner.
            Assert.True(outcome.Reloaded, outcome.Reason);
            Assert.False(outcome.Drained);
            // Exactly one line for the stuck handler, naming it and the extension, and the reload
            // carried on regardless.
            string line = Assert.Single(
                fixture.Log.Matching("did not return within"),
                message => message.Contains("ext:acme-tools:ping", StringComparison.Ordinal)
            );
            Assert.Contains("reload", line, StringComparison.Ordinal);
            Assert.Contains("acme-tools", line, StringComparison.Ordinal);
            Assert.Equal(1, fixture.Log.Count("did not return within"));
        }
        finally
        {
            File.WriteAllText(release, string.Empty);
            finished = await invocation.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        }

        // The handler finishes later; its result is discarded, never rendered.
        Assert.Null(finished.Message);
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

using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>
/// Task 3.3: <c>/extensions reload</c> is a preflight and a swap, in that order. Trust for the bytes
/// on disk comes first, then a build against the lock bound to those bytes — and only a successful
/// build touches the running extension. A failed compile or a file that is no longer trusted reports,
/// and the old build stays loaded and callable, with <c>builds.json</c> still pointing at it.
/// </summary>
public sealed class SingleFileReloadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_failed_build_reports_and_leaves_the_old_build_loaded_and_callable()
    {
        using SingleFileLoadFixture fixture = SingleFileLoadFixture.Create();
        fixture.AddToolPackage(SingleFileLoadFixture.Lib, Ct);
        SingleFileBuildOutcome first = await fixture.BuildAsync(
            SingleFileLoadFixture.Source(probe: "\"first\""),
            cancellationToken: Ct
        );
        ExtensionLoader loader = fixture.Discovered();
        await loader.LoadSingleFileAsync(fixture.Id, Ct);
        Assert.Equal("first", (await CommandAsync(loader, "ping")).Message);

        // The edit compiles to nothing: the header is valid and the bytes are trusted, so the
        // preflight reaches the compiler and stops there.
        fixture.Write(
            SingleFileLoadFixture.Source(
                probe: "\"second\"",
                commandBody: "int broken = Missing.OnPurpose; return CommandResult.None;"
            )
        );
        // The edit changed the resolution the lock recorded, so the restore asks — an interactive
        // reload asks too, and the user's approval is what the preflight needs.
        SingleFileReloadOutcome outcome = await loader.ReloadSingleFileAsync(
            fixture.Id,
            fixture.Builder(),
            new ApprovingPrompt(),
            Ct
        );

        Assert.False(outcome.Reloaded);
        Assert.Contains("error CS", outcome.Reason!, StringComparison.Ordinal);
        // The build that is current on disk is still the one that was loaded, and the extension is
        // still answering from it.
        Assert.Equal(first.Build!.Key, fixture.BuildCache().Current(fixture.Id)!.Key);
        Assert.Equal(first.Build.Key, loader.LoadedSingleFileKey(fixture.Id));
        Assert.Equal("first", (await CommandAsync(loader, "ping")).Message);
    }

    [Fact]
    public async Task A_refused_trust_preflight_stops_before_the_compiler_and_keeps_the_old_build()
    {
        using SingleFileLoadFixture fixture = SingleFileLoadFixture.Create();
        fixture.AddToolPackage(SingleFileLoadFixture.Lib, Ct);
        SingleFileBuildOutcome first = await fixture.BuildAsync(
            SingleFileLoadFixture.Source(probe: "\"first\""),
            cancellationToken: Ct
        );
        int compilesBefore = fixture.Compiler.Creations;
        ExtensionLoader loader = fixture.Discovered();
        await loader.LoadSingleFileAsync(fixture.Id, Ct);

        // Edited, not approved: the bytes on disk are no longer the bytes anyone trusted.
        File.WriteAllText(fixture.SourcePath, SingleFileLoadFixture.Source(probe: "\"second\""));
        SingleFileReloadOutcome outcome = await loader.ReloadSingleFileAsync(
            fixture.Id,
            fixture.Builder(),
            new DecliningPrompt(),
            Ct
        );

        Assert.False(outcome.Reloaded);
        Assert.Contains("did not trust", outcome.Reason!, StringComparison.Ordinal);
        Assert.Equal(compilesBefore, fixture.Compiler.Creations);
        Assert.Equal(first.Build!.Key, fixture.BuildCache().Current(fixture.Id)!.Key);
        Assert.Equal("first", (await CommandAsync(loader, "ping")).Message);
    }

    [Fact]
    public async Task A_trusted_reload_swaps_the_build_and_the_next_call_sees_it()
    {
        using SingleFileLoadFixture fixture = SingleFileLoadFixture.Create();
        fixture.AddToolPackage(SingleFileLoadFixture.Lib, Ct);
        SingleFileBuildOutcome first = await fixture.BuildAsync(
            SingleFileLoadFixture.Source(probe: "\"first\""),
            cancellationToken: Ct
        );
        ExtensionLoader loader = fixture.Discovered();
        await loader.LoadSingleFileAsync(fixture.Id, Ct);

        await fixture.BuildAsync(
            SingleFileLoadFixture.Source(probe: "\"second\""),
            new ApprovingPrompt(),
            Ct
        );
        SingleFileReloadOutcome outcome = await loader.ReloadSingleFileAsync(
            fixture.Id,
            fixture.Builder(),
            new ApprovingPrompt(),
            Ct
        );

        Assert.True(outcome.Reloaded, outcome.Reason);
        Assert.NotEqual(first.Build!.Key, outcome.LoadedBuildKey);
        Assert.Equal("second", (await CommandAsync(loader, "ping")).Message);
        Assert.Equal(outcome.LoadedBuildKey, loader.LoadedSingleFileKey(fixture.Id));
    }

    [Fact]
    public async Task An_unknown_id_reports_without_throwing()
    {
        using SingleFileLoadFixture fixture = SingleFileLoadFixture.Create();
        ExtensionLoader loader = fixture.Discovered();

        SingleFileReloadOutcome outcome = await loader.ReloadSingleFileAsync(
            "nope",
            fixture.Builder(),
            prompt: null,
            Ct
        );

        Assert.False(outcome.Reloaded);
        Assert.Contains(
            "not a discovered single-file extension",
            outcome.Reason!,
            StringComparison.Ordinal
        );
    }

    private static async Task<CommandResult> CommandAsync(ExtensionLoader loader, string name)
    {
        ICommandSource source = Assert.Single(loader.CommandSources.List()).Source;
        return await source
            .InvokeAsync(new CommandInvocation(name, string.Empty), Ct)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }
}

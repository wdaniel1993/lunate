namespace Lunate.Extensibility.Compile.Tests;

/// <summary>
/// A build in the cache is proof the file was built, never proof that its bytes were approved
/// (spec: "Trust before restore" and "The model never approves trust"): a file with no trust
/// decision is refused before its assembly is read, even when a build for exactly those bytes sits
/// in the cache — the state <c>/trust clear</c> used to leave behind.
/// </summary>
public sealed class SingleFileTrustTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_warm_cache_does_not_load_an_untrusted_file()
    {
        using SingleFileLoadFixture fixture = SingleFileLoadFixture.Create();
        SingleFileBuildOutcome built = await fixture.BuildAsync(
            SingleFileLoadFixture.Source(probe: "\"untrusted\""),
            cancellationToken: Ct
        );
        Assert.True(File.Exists(built.Build!.AssemblyPath));

        // The decision is gone while the build stays: deleted, cleared, or never recorded.
        File.Delete(Path.Combine(fixture.Store, "trust.json"));

        ExtensionLoader loader = fixture.Discovered();
        SingleFileExtension row = Assert.Single(loader.SingleFileExtensions);
        Assert.Equal(SingleFileExtensionState.NotBuilt, row.State);
        Assert.Null(row.BuildKey);
        Assert.Null(row.AssemblyPath);

        ExtensionTrustDeniedException refused =
            await Assert.ThrowsAsync<ExtensionTrustDeniedException>(() =>
                loader.LoadSingleFileAsync(fixture.Id, Ct)
            );

        Assert.Contains("has no trust decision", refused.Message, StringComparison.Ordinal);
        Assert.Contains(fixture.Id, refused.Message, StringComparison.Ordinal);
        // Nothing of the extension ran: no tool and no command was registered.
        Assert.Empty(loader.Tools.List());
        Assert.Empty(loader.CommandSources.List());
    }
}

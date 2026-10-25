using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// The whole chain over the real engine and a real (local) feed: discovery reads the file, the
/// first trust covers the source, the restore writes <c>extension.lock.json</c> and binds its hash
/// into the trust record with no second question, and the resolution is byte-for-byte the same
/// whether it came from a cold or a warm packages folder.
/// </summary>
public sealed class SingleFileRestoreEndToEndTests
{
    private const string Header = """
        #:extension acme-tools@1.0.0
        #:tool fetch_issue
        #:package Acme.Tools@[2.0.0,3.0.0)

        public static class Acme
        {
            public static void Register() { }
        }

        """;

    private static readonly SingleFileTarget Target = SingleFileTarget.Of(
        new Version(10, 0, 4),
        "osx-arm64"
    );

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_trust_restores_writes_the_lock_and_binds_it_with_one_prompt()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Tools", "2.0.0")
            .AddReferencePack("10.0.4");
        string store = System.IO.Path.Combine(temp.Root, "store");
        string file = WriteFile(store);
        var prompt = new ApprovingPrompt();

        SingleFileRestoreOutcome outcome = await Restorer(feed, store)
            .EnsureRestoredAsync(file, ExtensionScope.Global, prompt, Ct);

        Assert.Equal(SingleFileRestoreStatus.Restored, outcome.Status);
        Assert.Equal(1, prompt.Calls);
        ExtensionLock @lock = outcome.Lock!;
        Assert.Equal("Acme.Tools@[2.0.0,3.0.0)", Assert.Single(@lock.PackagesWritten));
        ExtensionLockPackage package = Assert.Single(@lock.Packages);
        Assert.Equal("2.0.0", package.Version);
        Assert.Equal(feed.Sha512("Acme.Tools", "2.0.0"), package.Sha512);
        Assert.Equal(["local"], @lock.Sources);
        Assert.Equal("10.0.4", @lock.Toolset.ReferencePack.Version);
        Assert.Equal(ReferencePackRules.Exact, @lock.Toolset.ReferencePack.Rule);
        Assert.Equal(
            feed.Sha512(ReferencePackRule.PackageId, "10.0.4"),
            @lock.Toolset.ReferencePack.Sha512
        );
        Assert.Equal(Target.Tfm, @lock.Toolset.Tfm);
        Assert.Equal(Target.Rid, @lock.Toolset.Rid);

        // The lock is the cache entry's, and what it says is what is on disk.
        string lockPath = System.IO.Path.Combine(
            store,
            "cache",
            "extensions",
            "acme-tools",
            ExtensionLock.FileName
        );
        Assert.Equal(lockPath, outcome.LockPath);
        Assert.Equal(ExtensionLockJson.Write(@lock), File.ReadAllText(lockPath));

        // The bind: a second restorer finds the file trusted for exactly these bytes and reads the
        // lock instead of resolving again — no engine, no prompt, no feed.
        var second = new CountingRestorer(feed, store);
        SingleFileRestoreOutcome again = await second.Restorer.EnsureRestoredAsync(
            file,
            ExtensionScope.Global,
            prompt: null,
            Ct
        );
        Assert.Equal(SingleFileRestoreStatus.Restored, again.Status);
        Assert.Equal(0, second.EngineConstructions);
        Assert.Empty(second.FeedRequests);
        Assert.Equal(1, prompt.Calls);
    }

    [Fact]
    public async Task Two_restores_of_the_same_source_write_the_same_lock_bytes()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Tools", "2.0.0", dependencies: ["Acme.Lib@1.0.0"])
            .Add("Acme.Lib", "1.0.0")
            .AddReferencePack("10.0.4");
        string cold = System.IO.Path.Combine(temp.Root, "cold");
        string warm = System.IO.Path.Combine(temp.Root, "warm");

        SingleFileRestoreOutcome first = await Restorer(feed, cold)
            .EnsureRestoredAsync(WriteFile(cold), ExtensionScope.Global, new ApprovingPrompt(), Ct);
        SingleFileRestoreOutcome second = await Restorer(feed, warm)
            .EnsureRestoredAsync(WriteFile(warm), ExtensionScope.Global, new ApprovingPrompt(), Ct);

        Assert.Equal(SingleFileRestoreStatus.Restored, first.Status);
        Assert.Equal(SingleFileRestoreStatus.Restored, second.Status);
        Assert.Equal(File.ReadAllBytes(first.LockPath!), File.ReadAllBytes(second.LockPath!));
    }

    [Fact]
    public async Task A_stale_file_stops_before_the_engine_exists_so_no_feed_is_asked()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Tools", "2.0.0")
            .AddReferencePack("10.0.4");
        string store = System.IO.Path.Combine(temp.Root, "store");
        string file = WriteFile(store);
        await Restorer(feed, store)
            .EnsureRestoredAsync(file, ExtensionScope.Global, new ApprovingPrompt(), Ct);

        File.AppendAllText(file, "// one edit\n");
        var stale = new CountingRestorer(feed, store);

        SingleFileRestoreOutcome outcome = await stale.Restorer.EnsureRestoredAsync(
            file,
            ExtensionScope.Global,
            prompt: null,
            Ct
        );

        Assert.Equal(SingleFileRestoreStatus.TrustRequired, outcome.Status);
        Assert.Equal(0, stale.EngineConstructions);
        Assert.Empty(stale.FeedRequests);
    }

    [Fact]
    public async Task A_hand_edited_lock_stops_before_the_engine_exists_so_no_feed_is_asked()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Tools", "2.0.0")
            .AddReferencePack("10.0.4");
        string store = System.IO.Path.Combine(temp.Root, "store");
        string file = WriteFile(store);
        SingleFileRestoreOutcome restored = await Restorer(feed, store)
            .EnsureRestoredAsync(file, ExtensionScope.Global, new ApprovingPrompt(), Ct);

        string lockPath = restored.LockPath!;
        File.WriteAllText(
            lockPath,
            File.ReadAllText(lockPath).Replace("\"2.0.0\"", "\"2.0.1\"", StringComparison.Ordinal)
        );

        var stale = new CountingRestorer(feed, store);
        SingleFileRestoreOutcome outcome = await stale.Restorer.EnsureRestoredAsync(
            file,
            ExtensionScope.Global,
            prompt: null,
            Ct
        );

        Assert.Equal(SingleFileRestoreStatus.TrustRequired, outcome.Status);
        Assert.Equal(0, stale.EngineConstructions);
        Assert.Empty(stale.FeedRequests);
        Assert.Contains("\"2.0.1\"", File.ReadAllText(lockPath), StringComparison.Ordinal);
    }

    /// <summary>The restorer with the production engine, wired to one local feed and a temp packages folder.</summary>
    private static SingleFileExtensionRestorer Restorer(TestPackageFeed feed, string store) =>
        new(
            new ExtensionHostOptions { StorePath = store },
            () =>
                new NuGetRestoreEngine(
                    new NuGetRestoreEngineOptions
                    {
                        Sources = [feed.Source],
                        GlobalPackagesFolder = feed.GlobalPackages,
                    }
                ),
            Target
        );

    private static string WriteFile(string store)
    {
        string directory = System.IO.Path.Combine(store, "extensions");
        Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, "acme.cs");
        File.WriteAllText(path, Header);
        return path;
    }

    /// <summary>A restorer whose factory counts constructions and whose engine records feed requests.</summary>
    private sealed class CountingRestorer
    {
        internal CountingRestorer(TestPackageFeed feed, string store)
        {
            Restorer = new SingleFileExtensionRestorer(
                new ExtensionHostOptions { StorePath = store },
                () =>
                {
                    EngineConstructions++;
                    return new NuGetRestoreEngine(
                        new NuGetRestoreEngineOptions
                        {
                            Sources = [feed.Source],
                            GlobalPackagesFolder = feed.GlobalPackages,
                            OnFeedRequest = FeedRequests.Add,
                        }
                    );
                },
                Target
            );
        }

        internal SingleFileExtensionRestorer Restorer { get; }

        internal int EngineConstructions { get; private set; }

        internal List<string> FeedRequests { get; } = [];
    }

    private sealed class ApprovingPrompt : IExtensionTrustPrompt
    {
        internal int Calls { get; private set; }

        public ValueTask<bool> ApproveSingleFileAsync(
            SingleFileTrustRequest request,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromResult(true);
        }
    }
}

using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// What the walk refuses and what it excludes (spec: "Dependency resolution"): a dependency no feed
/// serves and a graph the resolver cannot settle each refuse the install before anything is written,
/// naming the dependency and the feeds tried; and an assembly the host owns never reaches the lock's
/// graph or the extension directory, because the host's copy is the one a load binds.
/// </summary>
public sealed class PackageDependencyGraphTests
{
    private static readonly DateTimeOffset Installed = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_dependency_no_feed_serves_refuses_the_install_and_writes_nothing()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle()
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .DependsOn("Acme.Missing")
            .Save();
        string store = Path.Combine(temp.Root, "store");

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                feed.Installer(now: Installed).InstallAsync(Request(store), Ct)
        );

        // The dependency and the feeds tried, from the walker's own refusal, and the package whose
        // install it was: what the user needs to fix it.
        Assert.Contains("Acme.Missing", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("local", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Acme.Toolkit", refusal.Message, StringComparison.Ordinal);
        NothingWritten(store);
    }

    [Fact]
    public async Task A_graph_the_resolver_cannot_settle_refuses_the_install()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        new TestPackageFeed(temp.Root)
            .Add("Acme.Shared", "1.0.0")
            .Add("Acme.Shared", "2.0.0")
            .Add("Acme.Left", "1.0.0", dependencies: ["Acme.Shared@[1.0.0,1.5.0)"])
            .Add("Acme.Right", "1.0.0", dependencies: ["Acme.Shared@[2.0.0,)"]);
        feed.Bundle()
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .DependsOn("Acme.Left", "Acme.Right")
            .Save();
        string store = Path.Combine(temp.Root, "store");

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                feed.Installer(now: Installed).InstallAsync(Request(store), Ct)
        );

        // NuGet's own conflict code and the package that could not settle, so the refusal reads like
        // the restore error this replaces — plus what a publisher can do about it.
        Assert.Contains("NU1107", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Acme.Shared", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("local", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("republished", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Acme.Toolkit", refusal.Message, StringComparison.Ordinal);
        NothingWritten(store);
    }

    [Fact]
    public async Task A_dependency_that_ships_a_host_owned_assembly_is_not_materialised_or_locked()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        new TestPackageFeed(temp.Root).Add(
            "Acme.Lib",
            "1.0.0",
            files:
            [
                (
                    "lib/net10.0/Lunate.Extensibility.Abstractions.dll",
                    "a copy of the contract the host owns"
                ),
            ]
        );
        feed.Bundle()
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .DependsOn("Acme.Lib")
            .Save();
        string store = Path.Combine(temp.Root, "store");

        await feed.Installer(now: Installed).InstallAsync(Request(store), Ct);

        string extension = Path.Combine(store, "extensions", "acme-tools");
        Assert.True(File.Exists(Path.Combine(extension, "Acme.Lib.dll")));
        Assert.False(
            File.Exists(Path.Combine(extension, "Lunate.Extensibility.Abstractions.dll")),
            "the host owns that assembly; a copy beside the entry assembly would be ignored at load"
        );

        string lockPath = Path.Combine(
            store,
            "cache",
            "extensions",
            "acme-tools",
            ExtensionLock.FileName
        );
        ExtensionLock @lock =
            ExtensionLockJson.TryRead(lockPath, out string? problem)
            ?? throw new InvalidOperationException($"the lock could not be read: {problem}");

        // The package stays in the graph — it was resolved — but the assets the resolution would
        // have used are the host's, so the graph's asset lists name none of them; `files` still
        // covers the whole nupkg, which is what its SHA-512 answers for.
        ExtensionLockPackage lib = Assert.Single(@lock.Packages);
        Assert.Equal(["lib/net10.0/Acme.Lib.dll"], lib.Runtime);
        Assert.Equal(["lib/net10.0/Acme.Lib.dll"], lib.Compile);
        Assert.Empty(lib.Native);
    }

    [Fact]
    public async Task A_packaged_assembly_the_host_owns_is_not_in_the_lock_graph()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        new TestPackageFeed(temp.Root).Add("Lunate.Extensibility.Abstractions", "1.0.0");
        feed.Bundle()
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .DependsOn("Lunate.Extensibility.Abstractions")
            .Save();
        string store = Path.Combine(temp.Root, "store");

        PackageInstallResult result = await feed.Installer(now: Installed)
            .InstallAsync(Request(store), Ct);

        Assert.Equal(PackageInstallStatus.Installed, result.Status);
        string extension = Path.Combine(store, "extensions", "acme-tools");
        Assert.False(
            File.Exists(Path.Combine(extension, "Lunate.Extensibility.Abstractions.dll")),
            "the host's copy is the one a load binds"
        );
        Assert.Empty(result.Dependencies);
        Assert.DoesNotContain(
            "Lunate.Extensibility.Abstractions",
            File.ReadAllText(
                Path.Combine(store, "cache", "extensions", "acme-tools", ExtensionLock.FileName)
            ),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task A_package_that_carries_no_extension_cannot_hold_dependencies()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        new TestPackageFeed(temp.Root).Add("Acme.Lib", "1.0.0");
        feed.Bundle()
            .Manifest(TestPackage.SkillsManifest)
            .Skill("greek")
            .DependsOn("Acme.Lib")
            .Save();
        string store = Path.Combine(temp.Root, "store");

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                feed.Installer(now: Installed).InstallAsync(Request(store), Ct)
        );

        Assert.Contains("Acme.Lib", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("extension", refusal.Message, StringComparison.Ordinal);
        NothingWritten(store);
    }

    [Fact]
    public async Task Offline_fails_naming_the_missing_dependency_and_the_packages_folder()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle()
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .DependsOn("Acme.Lib")
            .Save();
        string store = Path.Combine(temp.Root, "store");
        // Warm the folder with the bundle alone: the package resolves, its dependency does not.
        string cached = feed.CachedNupkg("Acme.Toolkit", "1.4.2");
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        File.Copy(
            Path.Combine(feed.FeedDirectory, "Acme.Toolkit.1.4.2.nupkg"),
            cached,
            overwrite: true
        );
        File.WriteAllText(feed.CachedHash("Acme.Toolkit", "1.4.2"), FileHashes.Sha512(cached));
        List<string> requests = [];

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                feed.Installer(requests.Add, offline: true, now: Installed)
                    .InstallAsync(Request(store), Ct)
        );

        Assert.Contains("Acme.Lib", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(feed.GlobalPackages, refusal.Message, StringComparison.Ordinal);
        Assert.Empty(requests);
        NothingWritten(store);
    }

    /// <summary>A refusal writes no content, no record, no staging and no lock.</summary>
    private static void NothingWritten(string store)
    {
        Assert.False(Directory.Exists(Path.Combine(store, "extensions")), "no extension content");
        Assert.False(Directory.Exists(Path.Combine(store, "skills")), "no skill content");
        Assert.False(Directory.Exists(Path.Combine(store, "cache")), "no lock");
        Assert.False(
            File.Exists(
                Path.Combine(store, "packages", "acme.toolkit", PackageInstallRecordJson.FileName)
            ),
            "no record"
        );
    }

    private static PackageInstallRequest Request(string store) =>
        new()
        {
            Id = "Acme.Toolkit",
            Version = "1.4.2",
            Target = PackageInstallTarget.ForUser(store, "Acme.Toolkit"),
        };
}

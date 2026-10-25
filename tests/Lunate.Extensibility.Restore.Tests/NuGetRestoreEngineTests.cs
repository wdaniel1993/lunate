using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// The in-process restore (spec: "Restore, lock and compiled cache" and "Reference assemblies for
/// compilation"): real nupkgs from a local folder feed resolve, install into the global packages
/// folder, and come back as exactly what the lock records — exact versions, SHA-512, assets and
/// edges — with the reference pack restored once and reused.
/// </summary>
public sealed class NuGetRestoreEngineTests
{
    private static readonly SingleFileTarget Target = SingleFileTarget.Of(
        new Version(10, 0, 4),
        "osx-arm64"
    );

    [Fact]
    public async Task A_restore_installs_the_graph_into_the_global_packages_folder()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Tools", "2.0.0", dependencies: ["Acme.Lib@1.0.0"])
            .Add("Acme.Lib", "1.0.0")
            .AddReferencePack("10.0.4");
        List<string> requests = [];
        (NuGetRestoreEngine engine, SingleFileRestoreRequest request) = Compose(feed, requests);

        SingleFileRestoreResult result = await engine.RestoreAsync(
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(feed.GlobalPackages, result.GlobalPackagesFolder);
        Assert.Equal(2, result.Packages.Count);
        Assert.Contains("download Acme.Tools 2.0.0 from local", requests);
        Assert.Contains("download Acme.Lib 1.0.0 from local", requests);

        foreach (ExtensionLockPackage package in result.Packages)
        {
            string install = feed.InstallPath(package.Id, package.Version);
            Assert.True(Directory.Exists(install), $"{package.Id} was not installed at {install}");
            Assert.True(
                File.Exists(Path.Combine(install, $"{package.Id.ToLowerInvariant()}.nuspec")),
                $"{package.Id} has no nuspec beside it"
            );
            Assert.True(
                File.Exists(
                    Path.Combine(
                        install,
                        string.Concat(package.Id.ToLowerInvariant(), ".", package.Version, ".nupkg")
                    )
                ),
                $"{package.Id} has no nupkg beside it"
            );
        }

        ExtensionLockPackage tools = Assert.Single(
            result.Packages,
            package => package.Id == "Acme.Tools"
        );
        Assert.True(tools.Direct);
        ExtensionLockDependency edge = Assert.Single(tools.Dependencies);
        Assert.Equal("Acme.Lib", edge.Id);
        Assert.False(Assert.Single(result.Packages, package => package.Id == "Acme.Lib").Direct);
    }

    [Fact]
    public async Task The_lock_records_the_exact_version_the_package_hash_and_the_assets()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Tools", "2.0.0", analyzers: true, referenceAssemblies: true)
            .AddReferencePack("10.0.4");
        (NuGetRestoreEngine engine, SingleFileRestoreRequest request) = Compose(
            feed,
            [],
            range: true
        );

        SingleFileRestoreResult result = await engine.RestoreAsync(
            request,
            TestContext.Current.CancellationToken
        );

        ExtensionLockPackage package = Assert.Single(
            result.Packages,
            entry => entry.Id == "Acme.Tools"
        );
        Assert.Equal("2.0.0", package.Version);
        Assert.Equal("Acme.Tools@[2.0.0,3.0.0)", package.Requested);
        Assert.Equal(feed.Sha512("Acme.Tools", "2.0.0"), package.Sha512);
        Assert.Equal(["lib/net10.0/Acme.Tools.dll"], package.Compile);
        Assert.Equal(["lib/net10.0/Acme.Tools.dll"], package.Runtime);
        Assert.Equal(["analyzers/dotnet/cs/Acme.Tools.Analyzers.dll"], package.Analyzers);
        Assert.True(package.Generators);
        Assert.Contains("Acme.Tools.nuspec", package.Files);
        Assert.Empty(package.Native);

        // A package that ships native bits for this RID records exactly those.
        var withNative = new TestPackageFeed(temp.Path("native"))
            .Add("Acme.Native", "1.0.0", nativeRid: "osx-arm64")
            .AddReferencePack("10.0.4");
        (NuGetRestoreEngine nativeEngine, SingleFileRestoreRequest nativeRequest) = Compose(
            withNative,
            [],
            name: "Acme.Native",
            version: "1.0.0"
        );
        SingleFileRestoreResult nativeResult = await nativeEngine.RestoreAsync(
            nativeRequest,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(
            ["runtimes/osx-arm64/native/libAcme.Native.dylib"],
            Assert.Single(nativeResult.Packages).Native
        );
    }

    [Fact]
    public async Task The_reference_pack_is_restored_once_then_reused_without_a_feed_request()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Tools", "2.0.0")
            .AddReferencePack("10.0.4");
        List<string> first = [];
        (NuGetRestoreEngine engine, SingleFileRestoreRequest request) = Compose(feed, first);

        SingleFileRestoreResult result = await engine.RestoreAsync(
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("Microsoft.NETCore.App.Ref", result.ReferencePack.Id);
        Assert.Equal("10.0.4", result.ReferencePack.Version);
        Assert.Equal(ReferencePackRules.Exact, result.ReferencePack.Rule);
        Assert.Contains("ref/net10.0/System.Runtime.dll", result.ReferencePack.Assets);
        Assert.Contains(
            first,
            entry =>
                entry.StartsWith("download Microsoft.NETCore.App.Ref", StringComparison.Ordinal)
        );

        List<string> second = [];
        (NuGetRestoreEngine again, SingleFileRestoreRequest sameRequest) = Compose(feed, second);
        SingleFileRestoreResult reused = await again.RestoreAsync(
            sameRequest,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("10.0.4", reused.ReferencePack.Version);
        Assert.DoesNotContain(
            second,
            entry => entry.Contains("Microsoft.NETCore.App.Ref", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            second,
            entry => entry.StartsWith("download", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_pack_that_is_already_in_the_packages_folder_is_reused_with_no_feed_at_all()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root).Add("Acme.Tools", "2.0.0");
        // A pack somebody else's restore already extracted: no ref-pack feed is needed at all.
        string installed = Path.Combine(
            feed.GlobalPackages,
            "microsoft.netcore.app.ref",
            "10.0.4",
            "ref",
            "net10.0"
        );
        Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, "System.Runtime.dll"), "reference assembly");
        List<string> requests = [];
        (NuGetRestoreEngine engine, SingleFileRestoreRequest request) = Compose(feed, requests);

        SingleFileRestoreResult result = await engine.RestoreAsync(
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("10.0.4", result.ReferencePack.Version);
        Assert.Equal(["ref/net10.0/System.Runtime.dll"], result.ReferencePack.Assets);
        Assert.DoesNotContain(
            requests,
            entry => entry.Contains("Microsoft.NETCore.App.Ref", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_runtime_ahead_of_the_feed_uses_the_highest_pack_below_it()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Tools", "2.0.0")
            .AddReferencePack("10.0.4");
        List<string> requests = [];
        (NuGetRestoreEngine engine, SingleFileRestoreRequest request) = Compose(
            feed,
            requests,
            runtime: new Version(10, 0, 5)
        );

        SingleFileRestoreResult result = await engine.RestoreAsync(
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("10.0.4", result.ReferencePack.Version);
        Assert.Equal(ReferencePackRules.NearestBelow, result.ReferencePack.Rule);
    }

    [Fact]
    public async Task No_pack_of_the_runtime_s_minor_still_fails_and_names_the_runtime()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root).Add("Acme.Tools", "2.0.0").AddReferencePack("9.0.4");
        List<string> requests = [];
        (NuGetRestoreEngine engine, SingleFileRestoreRequest request) = Compose(
            feed,
            requests,
            runtime: new Version(10, 0, 5)
        );

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                engine.RestoreAsync(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "no Microsoft.NETCore.App.Ref is published for runtime 10.0.5",
            failure.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task A_missing_package_names_the_feeds_it_asked()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root).AddReferencePack("10.0.4");
        (NuGetRestoreEngine engine, SingleFileRestoreRequest request) = Compose(
            feed,
            [],
            name: "Acme.Missing"
        );

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                engine.RestoreAsync(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "package 'Acme.Missing' was not found on any configured feed (local)",
            failure.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task An_unsatisfiable_range_names_the_range_and_who_asked()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Lib", "1.0.0")
            .AddReferencePack("10.0.4");
        (NuGetRestoreEngine engine, SingleFileRestoreRequest request) = Compose(
            feed,
            [],
            name: "Acme.Lib",
            version: "[2.0.0, )"
        );

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                engine.RestoreAsync(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "no version of 'Acme.Lib' satisfies",
            failure.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("[2.0.0, )", failure.Message, StringComparison.Ordinal);
        Assert.Contains("asked by Acme.Lib@[2.0.0, )", failure.Message, StringComparison.Ordinal);
    }

    private static (NuGetRestoreEngine Engine, SingleFileRestoreRequest Request) Compose(
        TestPackageFeed feed,
        List<string> requests,
        bool range = false,
        string name = "Acme.Tools",
        string? version = null,
        Version? runtime = null,
        string rid = "osx-arm64"
    )
    {
        string declared = version ?? (range ? "[2.0.0,3.0.0)" : "2.0.0");
        var engine = new NuGetRestoreEngine(
            new NuGetRestoreEngineOptions
            {
                Sources = [feed.Source],
                GlobalPackagesFolder = feed.GlobalPackages,
                OnFeedRequest = requests.Add,
            }
        );
        var request = new SingleFileRestoreRequest
        {
            ExtensionId = "acme-tools",
            ExtensionVersion = "1.0.0",
            SourceDigest = "digest",
            SourcePath = System.IO.Path.Combine(feed.Root, "acme.cs"),
            Packages =
            [
                new ExtensionPackageReference(
                    name,
                    declared,
                    FormattableString.Invariant($"{name}@{declared}")
                ),
            ],
            Target = SingleFileTarget.Of(runtime ?? new Version(10, 0, 4), rid),
        };
        return (engine, request);
    }
}

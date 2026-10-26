using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// A package's declared dependencies (spec: "Dependency resolution", design D3–D5): the walker that
/// resolves a single file's <c>#:package</c> lines resolves a package's nuspec dependencies too, the
/// resolved assemblies are materialised beside the entry assembly — the layout the packaged loader
/// already binds — and the lock lands in the extension's cache entry in the existing shape. The
/// phase-1 refusal is gone, so the package it refused installs; the folders no manifest kind names
/// are still not installed, and are named once.
/// </summary>
public sealed class PackageDependencyTests
{
    private static readonly DateTimeOffset Installed = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_declared_dependency_and_its_own_dependency_are_materialised()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        new TestPackageFeed(temp.Root)
            .Add("Acme.Core", "1.0.0")
            .Add(
                "Acme.Lib",
                "1.0.0",
                dependencies: ["Acme.Core@1.0.0"],
                files: [("lib/net10.0/de/Acme.Lib.resources.dll", "satellite bytes")]
            );
        feed.Bundle()
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .DependsOn("Acme.Lib")
            .Save();
        string store = Path.Combine(temp.Root, "store");

        PackageInstallResult result = await feed.Installer(now: Installed)
            .InstallAsync(Request(store), Ct);

        Assert.Equal(PackageInstallStatus.Installed, result.Status);
        string extension = Path.Combine(store, "extensions", "acme-tools");
        Assert.True(File.Exists(Path.Combine(extension, "acme-tools.dll")), "the entry assembly");
        Assert.True(
            File.Exists(Path.Combine(extension, "Acme.Lib.dll")),
            "the declared dependency"
        );
        Assert.True(
            File.Exists(Path.Combine(extension, "Acme.Core.dll")),
            "the dependency's own dependency"
        );

        // The closure the command prints: id and the exact version the walk chose, ordered by id.
        Assert.Equal(
            [("Acme.Core", "1.0.0"), ("Acme.Lib", "1.0.0")],
            [.. result.Dependencies.Select(dependency => (dependency.Id, dependency.Version))]
        );

        // A satellite keeps its culture folder: a load looks for it under the culture, not beside
        // the entry assembly.
        Assert.True(
            File.Exists(Path.Combine(extension, "de", "Acme.Lib.resources.dll")),
            "the satellite asset"
        );

        // A materialised assembly is content like any other: the record names it and hashes it, so
        // uninstall can take it back and --verify can prove it.
        foreach (
            string name in new[] { "Acme.Lib.dll", "Acme.Core.dll", "de/Acme.Lib.resources.dll" }
        )
        {
            InstalledFile file = Assert.Single(
                result.Record.Content.Paths,
                entry => entry.Path == $"extensions/acme-tools/{name}"
            );
            string path = Path.Combine(extension, name.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(new FileInfo(path).Length, file.Size);
            Assert.Equal(BundleFeed.Sha256(path), file.Sha256);
        }
    }

    [Fact]
    public async Task The_lock_names_the_exact_versions_in_the_existing_shape()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        new TestPackageFeed(temp.Root)
            .Add("Acme.Core", "1.0.0")
            .Add("Acme.Lib", "1.0.0", dependencies: ["Acme.Core@1.0.0"]);
        feed.Bundle()
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .DependsOn("Acme.Lib")
            .Save();
        string store = Path.Combine(temp.Root, "store");

        await feed.Installer(now: Installed).InstallAsync(Request(store), Ct);

        string lockPath = Path.Combine(
            store,
            "cache",
            "extensions",
            "acme-tools",
            ExtensionLock.FileName
        );
        Assert.True(File.Exists(lockPath), "the lock lives in the extension's cache entry");
        ExtensionLock @lock =
            ExtensionLockJson.TryRead(lockPath, out string? problem)
            ?? throw new InvalidOperationException($"the lock could not be read: {problem}");

        Assert.Equal("acme-tools", @lock.ExtensionId);
        Assert.Equal("0.1.0", @lock.ExtensionVersion);
        Assert.Equal(feed.Sha512("Acme.Toolkit", "1.4.2"), @lock.SourceDigest);
        Assert.Equal([("Acme.Core", "1.0.0"), ("Acme.Lib", "1.0.0")], Versions(@lock));
        Assert.Equal(["local"], @lock.Sources);

        // The declared edge is direct and keeps the form it was declared with; the package it
        // brought is transitive and carries the edge that asked for it.
        ExtensionLockPackage lib = Assert.Single(
            @lock.Packages,
            package => package.Id == "Acme.Lib"
        );
        Assert.True(lib.Direct);
        Assert.Equal("Acme.Lib@[1.0.0, )", lib.Requested);
        Assert.Equal(
            [("Acme.Core", "[1.0.0, )")],
            [.. lib.Dependencies.Select(edge => (edge.Id, edge.Range))]
        );
        Assert.Equal(feed.Sha512("Acme.Lib", "1.0.0"), lib.Sha512);
        Assert.Contains("lib/net10.0/Acme.Lib.dll", lib.Runtime, StringComparer.Ordinal);
        Assert.Contains("lib/net10.0/Acme.Lib.dll", lib.Files, StringComparer.Ordinal);
        Assert.False(
            Assert.Single(@lock.Packages, package => package.Id == "Acme.Core").Direct,
            "a dependency of a dependency is not a direct declaration"
        );

        // The toolset half names the machine the graph was resolved for, not a compile target.
        SingleFileTarget target = SingleFileTarget.Current();
        Assert.Equal(target.Tfm, @lock.Toolset.Tfm);
        Assert.Equal(target.Rid, @lock.Toolset.Rid);
        Assert.Equal(target.Runtime, @lock.Toolset.Runtime);
        Assert.Equal(ReferencePackRules.NotCompiled, @lock.Toolset.ReferencePack.Rule);

        // Canonical bytes: LF, no BOM, and re-writing the parsed lock reproduces the file exactly.
        byte[] bytes = File.ReadAllBytes(lockPath);
        Assert.Equal(ExtensionLockJson.ToBytes(@lock), bytes);
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    [Fact]
    public async Task Offline_resolves_the_whole_closure_with_zero_feed_requests()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        new TestPackageFeed(temp.Root)
            .Add("Acme.Core", "1.0.0")
            .Add("Acme.Lib", "1.0.0", dependencies: ["Acme.Core@1.0.0"]);
        feed.Bundle()
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .DependsOn("Acme.Lib")
            .Save();
        string store = Path.Combine(temp.Root, "store");
        await feed.Installer(now: Installed).InstallAsync(Request(store), Ct);
        Directory.Delete(store, recursive: true);

        List<string> requests = [];
        PackageInstallResult result = await feed.Installer(
                requests.Add,
                offline: true,
                now: Installed
            )
            .InstallAsync(Request(store), Ct);

        Assert.Equal(PackageInstallStatus.Installed, result.Status);
        Assert.Empty(requests);
        Assert.True(File.Exists(Path.Combine(store, "extensions", "acme-tools", "Acme.Lib.dll")));
        Assert.True(File.Exists(Path.Combine(store, "extensions", "acme-tools", "Acme.Core.dll")));
        Assert.True(
            File.Exists(
                Path.Combine(store, "cache", "extensions", "acme-tools", ExtensionLock.FileName)
            )
        );
    }

    [Fact]
    public void Undeclared_folders_are_ignored_and_named_once()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .File("build/Acme.Toolkit.targets", "<Project />")
            .File("buildTransitive/Acme.Toolkit.targets", "<Project />")
            .File("lib/net10.0/Acme.Toolkit.dll", "assembly bytes")
            .Save();

        PackageInfo info = PackageReader.Read(nupkg);

        Assert.Equal(["build", "buildTransitive", "lib"], [.. info.IgnoredFolders]);
        Assert.Equal("acme-tools", info.Extension!.Manifest.Id);
        Assert.Equal(["greek"], [.. info.Skills.Select(skill => skill.Name)]);

        string report = PackageReport.Describe(info).ReplaceLineEndings("\n");
        string ignored = Assert.Single(
            report.Split('\n'),
            line => line.StartsWith("ignored:", StringComparison.Ordinal)
        );
        Assert.Contains("build/", ignored, StringComparison.Ordinal);
        Assert.Contains("buildTransitive/", ignored, StringComparison.Ordinal);
        Assert.Contains("lib/", ignored, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(report, "buildTransitive/"));
        Assert.Equal(1, CountOccurrences(report, "lib/"));
    }

    [Fact]
    public void A_package_without_undeclared_folders_prints_no_ignored_line()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .Save();

        PackageInfo info = PackageReader.Read(nupkg);

        Assert.Empty(info.IgnoredFolders);
        Assert.DoesNotContain(
            PackageReport.Describe(info).ReplaceLineEndings("\n"),
            "ignored:",
            StringComparison.Ordinal
        );
    }

    /// <summary>The lock's packages as <c>(id, version)</c>, in the order the lock keeps them.</summary>
    private static (string Id, string Version)[] Versions(ExtensionLock @lock) =>
        [.. @lock.Packages.Select(package => (package.Id, package.Version))];

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

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static PackageInstallRequest Request(string store) =>
        new()
        {
            Id = "Acme.Toolkit",
            Version = "1.4.2",
            Target = PackageInstallTarget.ForUser(store, "Acme.Toolkit"),
        };
}

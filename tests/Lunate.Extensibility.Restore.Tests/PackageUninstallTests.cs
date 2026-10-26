namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// Uninstall (spec: "Uninstall"): exactly the recorded paths go, the nupkgs and the dependency lock
/// stay, a file the package did not write is left and named, an edited recorded file refuses without
/// <c>--force</c>, an extension id resolves to the one package that names it, and <c>--force</c> on
/// an install repairs a missing recorded file from the recorded version.
/// </summary>
public sealed class PackageUninstallTests
{
    private static readonly DateTimeOffset Installed = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Uninstall_deletes_the_recorded_paths_and_leaves_the_nupkgs_and_the_lock()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle()
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .Skill("roman")
            .Save();
        string store = Path.Combine(temp.Root, "store");
        PackageInstallResult install = await feed.Installer(now: Installed)
            .InstallAsync(Request(store, "Acme.Toolkit"), Ct);
        string nupkg = feed.CachedNupkg("Acme.Toolkit", "1.4.2");
        string @lock = Path.Combine(
            store,
            "cache",
            "extensions",
            "acme-tools",
            ExtensionLock.FileName
        );
        Assert.True(File.Exists(nupkg));
        Assert.True(File.Exists(@lock));

        PackageUninstallResult result = Uninstall(store, "Acme.Toolkit");

        Assert.Equal(4, result.RemovedFiles.Count);
        Assert.Empty(result.LeftFiles);
        Assert.Empty(result.KeptDirectories);
        Assert.True(result.RecordRemoved);
        Assert.Equal(install.RecordPath, result.RecordPath);
        Assert.False(File.Exists(install.RecordPath));
        Assert.False(
            Directory.Exists(Path.Combine(store, "packages", "acme.toolkit")),
            "the record's own directory goes with it"
        );
        Assert.False(Directory.Exists(Path.Combine(store, "extensions", "acme-tools")));
        Assert.False(Directory.Exists(Path.Combine(store, "skills", "greek")));
        Assert.False(Directory.Exists(Path.Combine(store, "skills", "roman")));

        // What is not installed content stays: the nupkg is NuGet's cache, the lock is the record of
        // a resolution, and a reinstall reuses both.
        Assert.True(File.Exists(nupkg));
        Assert.True(File.Exists(@lock));
    }

    [Fact]
    public async Task Uninstall_removes_the_closure_it_materialised_and_keeps_the_lock()
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
        await feed.Installer(now: Installed).InstallAsync(Request(store, "Acme.Toolkit"), Ct);
        string extension = Path.Combine(store, "extensions", "acme-tools");
        Assert.True(File.Exists(Path.Combine(extension, "Acme.Lib.dll")));
        string @lock = Path.Combine(
            store,
            "cache",
            "extensions",
            "acme-tools",
            ExtensionLock.FileName
        );

        PackageUninstallResult result = Uninstall(store, "Acme.Toolkit");

        Assert.Contains(
            Path.Combine(extension, "Acme.Lib.dll"),
            result.RemovedFiles,
            StringComparer.Ordinal
        );
        Assert.Contains(
            Path.Combine(extension, "Acme.Core.dll"),
            result.RemovedFiles,
            StringComparer.Ordinal
        );
        Assert.False(Directory.Exists(extension));
        Assert.True(File.Exists(@lock), "the lock is the record of a resolution, not content");
    }

    [Fact]
    public async Task A_hand_added_file_is_left_and_named()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        await feed.Installer(now: Installed).InstallAsync(Request(store, "Acme.Toolkit"), Ct);
        string extension = Path.Combine(store, "extensions", "acme-tools");
        string handAdded = Path.Combine(extension, "notes.txt");
        File.WriteAllText(handAdded, "mine");

        PackageUninstallResult result = Uninstall(store, "Acme.Toolkit");

        Assert.True(File.Exists(handAdded), "the package did not write that file");
        Assert.Equal([handAdded], result.LeftFiles);
        Assert.Equal([extension], result.KeptDirectories);
        Assert.False(File.Exists(Path.Combine(extension, "acme-tools.dll")));
        Assert.False(File.Exists(Path.Combine(extension, "extension.json")));
        Assert.False(File.Exists(result.RecordPath));
        Assert.False(Directory.Exists(Path.Combine(store, "skills", "greek")));
    }

    [Fact]
    public async Task An_edited_recorded_file_refuses_and_is_removed_with_force()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        PackageInstallResult install = await feed.Installer(now: Installed)
            .InstallAsync(Request(store, "Acme.Toolkit"), Ct);
        string assembly = Path.Combine(store, "extensions", "acme-tools", "acme-tools.dll");
        File.WriteAllText(assembly, "edited by hand");

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
            Uninstall(store, "Acme.Toolkit")
        );

        Assert.Contains("acme-tools.dll", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--force", refusal.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(assembly), "a refused uninstall deletes nothing");
        Assert.True(File.Exists(install.RecordPath));
        Assert.True(File.Exists(Path.Combine(store, "skills", "greek", "SKILL.md")));

        PackageUninstallResult forced = Uninstall(store, "Acme.Toolkit", force: true);

        Assert.True(forced.Forced);
        Assert.False(File.Exists(assembly));
        Assert.False(File.Exists(install.RecordPath));
    }

    [Fact]
    public async Task An_extension_id_one_record_names_uninstalls_that_package()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        await feed.Installer(now: Installed).InstallAsync(Request(store, "Acme.Toolkit"), Ct);

        PackageUninstallResult result = Uninstall(store, "acme-tools");

        Assert.Equal("Acme.Toolkit", result.Record.Package.Id);
        Assert.False(File.Exists(result.RecordPath));
        Assert.False(Directory.Exists(Path.Combine(store, "extensions", "acme-tools")));
    }

    [Fact]
    public void An_extension_id_two_records_name_is_refused_naming_the_candidates()
    {
        using var temp = new TempDirectory();
        string store = Path.Combine(temp.Root, "store");
        Record(store, "First.Package", "1.0.0");
        Record(store, "Second.Package", "2.0.0");

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
            Uninstall(store, "acme-tools")
        );

        Assert.Contains("First.Package", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Second.Package", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Name the package id", refusal.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(store, "packages", "first.package", "install.json")));
        Assert.True(File.Exists(Path.Combine(store, "packages", "second.package", "install.json")));
    }

    [Fact]
    public void An_id_nothing_installed_is_refused_naming_the_store()
    {
        using var temp = new TempDirectory();
        string store = Path.Combine(temp.Root, "store");
        Directory.CreateDirectory(store);

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
            Uninstall(store, "Acme.Toolkit")
        );

        Assert.Contains("Acme.Toolkit", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(store, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Force_on_install_repairs_a_missing_recorded_file_and_keeps_the_record()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        PackageInstaller installer = feed.Installer(now: Installed);
        PackageInstallResult install = await installer.InstallAsync(
            Request(store, "Acme.Toolkit"),
            Ct
        );
        byte[] recordBefore = File.ReadAllBytes(install.RecordPath);
        string assembly = Path.Combine(store, "extensions", "acme-tools", "acme-tools.dll");
        File.Delete(assembly);
        InstalledFile recorded = Assert.Single(
            install.Record.Content.Paths,
            file => file.Path == "extensions/acme-tools/acme-tools.dll"
        );

        PackageInstallResult repaired = await installer.InstallAsync(
            Request(store, "Acme.Toolkit", force: true),
            Ct
        );

        Assert.Equal(PackageInstallStatus.Repaired, repaired.Status);
        Assert.True(File.Exists(assembly), "the recorded file was not rewritten");
        Assert.Equal(recorded.Sha256, BundleFeed.Sha256(assembly));
        Assert.Equal(recordBefore, File.ReadAllBytes(install.RecordPath));
        Assert.Equal(
            PackageInstallRecordJson.Write(install.Record),
            PackageInstallRecordJson.Write(repaired.Record)
        );
        Assert.True(PackageContentVerifier.Verify(repaired.Record, store).Clean);
    }

    private static PackageUninstallResult Uninstall(string store, string id, bool force = false) =>
        new PackageUninstaller().Uninstall(
            new PackageUninstallRequest
            {
                Id = id,
                StoreRoot = store,
                Force = force,
            }
        );

    private static PackageInstallRequest Request(string store, string id, bool force = false) =>
        new()
        {
            Id = id,
            Version = "1.4.2",
            Target = PackageInstallTarget.ForUser(store, id),
            Force = force,
        };

    /// <summary>
    /// A record written by hand, so a test can put two packages on one extension id — a state the
    /// installer itself refuses, and the ambiguity an uninstall by extension id has to survive.
    /// </summary>
    private static void Record(string store, string id, string version)
    {
        string path = Path.Combine(
            store,
            "packages",
            id.ToLowerInvariant(),
            PackageInstallRecordJson.FileName
        );
        PackageInstallRecordJson.WriteTo(
            path,
            new PackageInstallRecord
            {
                Package = new InstalledPackage
                {
                    Id = id,
                    Version = version,
                    Sha512 = "sha512",
                    Sources = ["local"],
                    Files = [],
                    ExtensionId = "acme-tools",
                },
                Content = new InstalledContent
                {
                    ExtensionId = "acme-tools",
                    ExtensionVersion = "0.1.0",
                    Skills = [],
                    Paths =
                    [
                        new InstalledFile(
                            "extensions/acme-tools/acme-tools.dll",
                            0,
                            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
                        ),
                    ],
                },
                Scope = PackageScope.User,
                InstalledAt = "2026-01-02T03:04:05.0000000+00:00",
            }
        );
    }
}

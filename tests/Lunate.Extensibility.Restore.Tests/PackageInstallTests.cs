namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// An install (spec: "The install record", "Where installed content lands"): an exact version lands
/// the extension and its skills under the scope root, the record names every file it wrote with its
/// size and hash, and a second install of the same version writes nothing.
/// </summary>
public sealed class PackageInstallTests
{
    private static readonly DateTimeOffset Installed = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_install_writes_the_extension_and_skills_and_records_every_path()
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

        PackageInstallResult result = await feed.Installer(now: Installed)
            .InstallAsync(Request(store, "Acme.Toolkit", "1.4.2"), Ct);

        Assert.Equal(PackageInstallStatus.Installed, result.Status);
        string extension = Path.Combine(store, "extensions", "acme-tools");
        Assert.True(File.Exists(Path.Combine(extension, "acme-tools.dll")));
        Assert.True(File.Exists(Path.Combine(extension, "extension.json")));
        Assert.True(File.Exists(Path.Combine(store, "skills", "greek", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(store, "skills", "roman", "SKILL.md")));

        string recordPath = Path.Combine(
            store,
            "packages",
            "acme.toolkit",
            PackageInstallRecordJson.FileName
        );
        Assert.Equal(recordPath, result.RecordPath);
        Assert.True(File.Exists(recordPath));

        // The package half: the sources used, the canonical id, the exact version, NuGet's SHA-512
        // for the nupkg that is in the packages folder, the archive's file list, the lock's entry.
        PackageInstallRecord record = result.Record;
        Assert.Equal("Acme.Toolkit", record.Package.Id);
        Assert.Equal("1.4.2", record.Package.Version);
        Assert.Equal(["local"], record.Package.Sources);
        Assert.Equal("acme-tools", record.Package.ExtensionId);
        Assert.Equal(feed.Sha512("Acme.Toolkit", "1.4.2"), record.Package.Sha512);
        Assert.Equal(
            FileHashes.Sha512(feed.CachedNupkg("Acme.Toolkit", "1.4.2")),
            record.Package.Sha512
        );
        Assert.Contains(PackageReader.ManifestFileName, record.Package.Files);
        Assert.Contains("extension/acme-tools.dll", record.Package.Files);
        Assert.Contains("skills/greek/SKILL.md", record.Package.Files);

        // The content half: what was installed and every written path, relative to the scope root.
        Assert.Equal("acme-tools", record.Content.ExtensionId);
        Assert.Equal("0.1.0", record.Content.ExtensionVersion);
        Assert.Equal(["greek", "roman"], record.Content.Skills);
        Assert.Equal(
            [
                "extensions/acme-tools/acme-tools.dll",
                "extensions/acme-tools/extension.json",
                "skills/greek/SKILL.md",
                "skills/roman/SKILL.md",
            ],
            [.. record.Content.Paths.Select(file => file.Path)]
        );
        foreach (InstalledFile file in record.Content.Paths)
        {
            string path = Path.Combine(store, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(new FileInfo(path).Length, file.Size);
            Assert.Equal(BundleFeed.Sha256(path), file.Sha256);
        }

        Assert.Equal(PackageScope.User, record.Scope);
        Assert.Null(record.Worktree);
        Assert.Equal("2026-01-02T03:04:05.0000000+00:00", record.InstalledAt);

        // Staging and the record agree: the record answers for itself and names exactly these files.
        PackageInstallRecord? read = PackageInstallRecordJson.TryRead(
            recordPath,
            out string? problem
        );
        Assert.Null(problem);
        Assert.Equal(PackageInstallRecordJson.Write(record), PackageInstallRecordJson.Write(read!));
        Assert.False(
            Directory.Exists(Path.Combine(store, "packages", "acme.toolkit", "staging")),
            "staging is deleted once the record is written"
        );
    }

    [Fact]
    public async Task A_skills_only_bundle_records_no_extension()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.SkillsManifest).Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");

        PackageInstallResult result = await feed.Installer(now: Installed)
            .InstallAsync(Request(store, "Acme.Toolkit", "1.4.2"), Ct);

        Assert.Null(result.Record.Package.ExtensionId);
        Assert.Null(result.Record.Content.ExtensionId);
        Assert.Null(result.Record.Content.ExtensionVersion);
        Assert.Equal(["greek"], result.Record.Content.Skills);
        Assert.Equal(
            ["skills/greek/SKILL.md"],
            [.. result.Record.Content.Paths.Select(f => f.Path)]
        );
        Assert.False(Directory.Exists(Path.Combine(store, "extensions")));
    }

    [Fact]
    public async Task A_project_install_records_paths_relative_to_the_worktree()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        string worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(worktree);

        PackageInstallResult result = await feed.Installer(now: Installed)
            .InstallAsync(
                new PackageInstallRequest
                {
                    Id = "Acme.Toolkit",
                    Version = "1.4.2",
                    Target = PackageInstallTarget.ForProject(worktree, store, "Acme.Toolkit"),
                },
                Ct
            );

        Assert.Equal(PackageScope.Project, result.Record.Scope);
        Assert.Equal(worktree, result.Record.Worktree);
        Assert.True(
            File.Exists(
                Path.Combine(worktree, ".lunate", "extensions", "acme-tools", "acme-tools.dll")
            )
        );
        Assert.True(File.Exists(Path.Combine(worktree, ".agents", "skills", "greek", "SKILL.md")));
        Assert.Equal(
            [
                ".agents/skills/greek/SKILL.md",
                ".lunate/extensions/acme-tools/acme-tools.dll",
                ".lunate/extensions/acme-tools/extension.json",
            ],
            [.. result.Record.Content.Paths.Select(file => file.Path)]
        );

        // The record is machine state: it lives in the store, not in the repository.
        Assert.Equal(
            Path.Combine(store, "packages", "acme.toolkit", PackageInstallRecordJson.FileName),
            result.RecordPath
        );
        Assert.False(Directory.Exists(Path.Combine(worktree, "packages")));
    }

    [Fact]
    public async Task Installing_the_same_version_again_reports_already_installed_and_moves_nothing()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        PackageInstaller installer = feed.Installer(now: Installed);
        PackageInstallResult first = await installer.InstallAsync(
            Request(store, "Acme.Toolkit", "1.4.2"),
            Ct
        );

        Dictionary<string, DateTime> before = Timestamps(store);
        byte[] recordBefore = File.ReadAllBytes(first.RecordPath);

        PackageInstallResult second = await installer.InstallAsync(
            Request(store, "Acme.Toolkit", "1.4.2"),
            Ct
        );

        Assert.Equal(PackageInstallStatus.AlreadyInstalled, second.Status);
        Assert.Equal(
            PackageInstallRecordJson.Write(first.Record),
            PackageInstallRecordJson.Write(second.Record)
        );
        Assert.Empty(second.WrittenPaths);
        Assert.Equal(recordBefore, File.ReadAllBytes(second.RecordPath));
        Assert.Equal(before, Timestamps(store));
    }

    [Fact]
    public async Task A_different_version_is_refused_without_touching_the_installed_content()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        feed.Bundle(version: "1.5.0")
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .Save();
        string store = Path.Combine(temp.Root, "store");
        PackageInstaller installer = feed.Installer(now: Installed);
        PackageInstallResult first = await installer.InstallAsync(
            Request(store, "Acme.Toolkit", "1.4.2"),
            Ct
        );
        Dictionary<string, DateTime> before = Timestamps(store);

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                installer.InstallAsync(Request(store, "Acme.Toolkit", "1.5.0"), Ct)
        );

        Assert.Contains("Acme.Toolkit", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("1.4.2", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("1.5.0", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(first.RecordPath, refusal.Message, StringComparison.Ordinal);
        Assert.Equal(before, Timestamps(store));
    }

    [Fact]
    public async Task A_hand_edited_record_is_refused_and_nothing_is_written()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        PackageInstaller installer = feed.Installer(now: Installed);
        PackageInstallResult first = await installer.InstallAsync(
            Request(store, "Acme.Toolkit", "1.4.2"),
            Ct
        );
        File.WriteAllText(
            first.RecordPath,
            File.ReadAllText(first.RecordPath)
                .Replace(
                    "\"version\": \"1.4.2\"",
                    "\"version\": \"1.4.3\"",
                    StringComparison.Ordinal
                )
        );
        Dictionary<string, DateTime> before = Timestamps(store);

        InvalidDataException refusal = await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.InstallAsync(Request(store, "Acme.Toolkit", "1.4.2"), Ct)
        );

        Assert.Contains(first.RecordPath, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("modified", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(before, Timestamps(store));
    }

    [Fact]
    public async Task A_version_that_is_not_exact_is_refused_before_any_feed_is_asked()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.SkillsManifest).Skill("greek").Save();
        List<string> requests = [];
        string store = Path.Combine(temp.Root, "store");

        ArgumentException refusal = await Assert.ThrowsAsync<ArgumentException>(() =>
            feed.Installer(observer: requests.Add)
                .InstallAsync(Request(store, "Acme.Toolkit", "[1.0.0,2.0.0)"), Ct)
        );

        Assert.Contains("[1.0.0,2.0.0)", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(requests);
        Assert.False(
            File.Exists(
                Path.Combine(store, "packages", "acme.toolkit", PackageInstallRecordJson.FileName)
            )
        );
    }

    private static PackageInstallRequest Request(string store, string id, string version) =>
        new()
        {
            Id = id,
            Version = version,
            Target = PackageInstallTarget.ForUser(store, id),
        };

    /// <summary>Every file under <paramref name="root"/> and its last write time, in a stable order.</summary>
    private static Dictionary<string, DateTime> Timestamps(string root) =>
        Directory
            .GetFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToDictionary(
                path => path,
                path => File.GetLastWriteTimeUtc(path),
                StringComparer.Ordinal
            );
}

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
            Directory
                .EnumerateDirectories(Path.Combine(store, "packages", "acme.toolkit"), "staging-*")
                .Any(),
            "the run's own staging is deleted once the record is written"
        );
    }

    [Fact]
    public async Task The_install_record_is_owner_only_on_posix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not exist on Windows.");
            return;
        }

        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");

        PackageInstallResult result = await feed.Installer(now: Installed)
            .InstallAsync(Request(store, "Acme.Toolkit", "1.4.2"), Ct);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(result.RecordPath)
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
    public async Task A_hand_edited_record_is_replaced_by_a_reinstall()
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

        // A record that no longer answers for itself does not block its own repair (addendum,
        // phase 6): the reinstall writes the content and a record that answers, and says that it
        // could not account for what the old one named.
        PackageInstallResult repaired = await installer.InstallAsync(
            Request(store, "Acme.Toolkit", "1.4.2"),
            Ct
        );

        Assert.Equal(PackageInstallStatus.Installed, repaired.Status);
        Assert.Empty(repaired.RemovedPaths);
        Assert.Contains(
            repaired.Warnings,
            warning => warning.Contains(first.RecordPath, StringComparison.Ordinal)
        );
        PackageInstallRecord? reread = PackageInstallRecordJson.TryRead(
            first.RecordPath,
            out string? problem
        );
        Assert.NotNull(reread);
        Assert.Null(problem);
        Assert.Equal("1.4.2", reread!.Package.Version);
        Assert.True(PackageContentVerifier.Verify(reread, store).Clean);
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

    /// <summary>
    /// A reinstall is the repair (spec: "The install record"): a record that cannot be read does not
    /// refuse the install, the run writes the content and a record that answers for itself, and it
    /// names the bytes it could not account for instead of claiming to have removed them.
    /// </summary>
    [Fact]
    public async Task A_reinstall_over_an_unreadable_record_writes_one_that_answers_for_itself()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        PackageInstallResult first = await feed.Installer(now: Installed)
            .InstallAsync(Request(store, "Acme.Toolkit", "1.4.2"), Ct);
        string recordPath = first.RecordPath;
        string extension = Path.Combine(store, "extensions", "acme-tools");

        // A record edited into nonsense, and a file a previous version of the package wrote that
        // nothing in the new archive corresponds to.
        File.WriteAllText(recordPath, "{ this is not a record");
        string leftover = Path.Combine(extension, "old-dependency.dll");
        File.WriteAllText(leftover, "bytes a previous version wrote");

        PackageInstallResult repaired = await feed.Installer(now: Installed)
            .InstallAsync(Request(store, "Acme.Toolkit", "1.4.2"), Ct);

        Assert.Equal(PackageInstallStatus.Installed, repaired.Status);
        // Nothing claims a removal: the record that named the old files could not be read.
        Assert.Empty(repaired.RemovedPaths);
        PackageInstallRecord? reread = PackageInstallRecordJson.TryRead(
            recordPath,
            out string? problem
        );
        Assert.NotNull(reread);
        Assert.Null(problem);
        Assert.True(PackageContentVerifier.Verify(reread!, store).Clean);

        // The bytes it could not account for are named, and they are still there.
        Assert.Contains(
            repaired.Warnings,
            warning => warning.Contains(leftover, StringComparison.Ordinal)
        );
        Assert.Contains(
            repaired.Warnings,
            warning => warning.Contains(recordPath, StringComparison.Ordinal)
        );
        Assert.True(File.Exists(leftover));
        Assert.True(
            File.Exists(Path.Combine(store, "skills", "greek", "SKILL.md")),
            "the skill this version provides landed"
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

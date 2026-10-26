namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// Upgrade and downgrade (spec: "Pinning and upgrade"): installing a different exact version of an
/// installed package is a pinned replacement — the old content is removed through the old record, the
/// new content and record are written, and a run that fails while downloading or resolving leaves the
/// previous install exactly as it was. The record diff is the proof, not the exit code.
/// </summary>
public sealed class PackageUpgradeTests
{
    private static readonly DateTimeOffset Installed = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_upgrade_removes_the_old_content_through_the_old_record()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle()
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .Skill("roman")
            .File("extension/only-in-1.4.2.txt", "a file the newer version does not carry")
            .Save();
        feed.Bundle(version: "1.5.0")
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .Save();
        string store = Path.Combine(temp.Root, "store");
        PackageInstaller installer = feed.Installer(now: Installed);
        await installer.InstallAsync(Request(store, "1.4.2"), Ct);

        PackageInstallResult result = await installer.InstallAsync(Request(store, "1.5.0"), Ct);

        Assert.Equal(PackageInstallStatus.Upgraded, result.Status);
        Assert.Equal("1.5.0", result.Record.Package.Version);
        Assert.Equal(
            [
                "extensions/acme-tools/acme-tools.dll",
                "extensions/acme-tools/extension.json",
                "skills/greek/SKILL.md",
            ],
            [.. result.Record.Content.Paths.Select(file => file.Path)]
        );
        Assert.Equal(["greek"], result.Record.Content.Skills);

        // Nothing the old version wrote is left: the stale file, the skill it carried and the
        // directory that held only them are gone.
        Assert.False(
            File.Exists(Path.Combine(store, "extensions", "acme-tools", "only-in-1.4.2.txt"))
        );
        Assert.False(Directory.Exists(Path.Combine(store, "skills", "roman")));
        Assert.True(File.Exists(Path.Combine(store, "skills", "greek", "SKILL.md")));

        // The record on disk answers for the content on disk.
        PackageInstallRecord? read = PackageInstallRecordJson.TryRead(
            result.RecordPath,
            out string? problem
        );
        Assert.Null(problem);
        Assert.Equal(
            PackageInstallRecordJson.Write(result.Record),
            PackageInstallRecordJson.Write(read!)
        );
        Assert.True(PackageContentVerifier.Verify(read!, store).Clean);
    }

    [Fact]
    public async Task An_upgrade_that_cannot_download_leaves_the_record_and_content_byte_identical()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        PackageInstaller installer = feed.Installer(now: Installed);
        PackageInstallResult first = await installer.InstallAsync(Request(store, "1.4.2"), Ct);
        Dictionary<string, string> before = Hashes(store);
        byte[] recordBefore = File.ReadAllBytes(first.RecordPath);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            installer.InstallAsync(Request(store, "9.9.9"), Ct)
        );

        Assert.Empty(Diff(before, Hashes(store)));
        Assert.Equal(recordBefore, File.ReadAllBytes(first.RecordPath));
        Assert.Equal(
            "1.4.2",
            PackageInstallRecordJson.TryRead(first.RecordPath, out _)!.Package.Version
        );
    }

    [Fact]
    public async Task An_upgrade_that_cannot_resolve_leaves_the_record_and_content_byte_identical()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        feed.Bundle(version: "1.5.0")
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .DependsOn("Acme.Nowhere")
            .Save();
        string store = Path.Combine(temp.Root, "store");
        PackageInstaller installer = feed.Installer(now: Installed);
        PackageInstallResult first = await installer.InstallAsync(Request(store, "1.4.2"), Ct);
        Dictionary<string, string> before = Hashes(store);
        byte[] recordBefore = File.ReadAllBytes(first.RecordPath);

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                installer.InstallAsync(Request(store, "1.5.0"), Ct)
        );

        Assert.Contains("Acme.Nowhere", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(Diff(before, Hashes(store)));
        Assert.Equal(recordBefore, File.ReadAllBytes(first.RecordPath));
    }

    [Fact]
    public async Task A_pinned_install_may_go_backwards()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle(version: "1.4.2")
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .Save();
        feed.Bundle(version: "1.5.0")
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .Skill("roman")
            .Save();
        string store = Path.Combine(temp.Root, "store");
        PackageInstaller installer = feed.Installer(now: Installed);
        await installer.InstallAsync(Request(store, "1.5.0"), Ct);

        PackageInstallResult result = await installer.InstallAsync(Request(store, "1.4.2"), Ct);

        Assert.Equal(PackageInstallStatus.Upgraded, result.Status);
        Assert.Equal("1.4.2", result.Record.Package.Version);
        Assert.Equal(["greek"], result.Record.Content.Skills);
        Assert.False(Directory.Exists(Path.Combine(store, "skills", "roman")));
        Assert.Equal(
            "1.4.2",
            PackageInstallRecordJson.TryRead(result.RecordPath, out _)!.Package.Version
        );
    }

    private static PackageInstallRequest Request(string store, string version) =>
        new()
        {
            Id = "Acme.Toolkit",
            Version = version,
            Target = PackageInstallTarget.ForUser(store, "Acme.Toolkit"),
        };

    /// <summary>Every file under <paramref name="root"/> and the SHA-256 of its bytes.</summary>
    private static Dictionary<string, string> Hashes(string root) =>
        Directory
            .GetFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToDictionary(path => path, BundleFeed.Sha256, StringComparer.Ordinal);

    /// <summary>The difference between two snapshots, one line per changed, missing or added file.</summary>
    private static List<string> Diff(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after
    )
    {
        List<string> diff = [];
        foreach ((string path, string hash) in before)
        {
            if (!after.TryGetValue(path, out string? now))
            {
                diff.Add($"missing: {path}");
            }
            else if (!string.Equals(hash, now, StringComparison.Ordinal))
            {
                diff.Add($"changed: {path}");
            }
        }

        foreach (string path in after.Keys)
        {
            if (!before.ContainsKey(path))
            {
                diff.Add($"added: {path}");
            }
        }

        return diff;
    }
}

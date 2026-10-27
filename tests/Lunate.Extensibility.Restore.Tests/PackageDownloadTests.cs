using System.Text;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// Download and its failures (spec: "Failure, offline and private feeds"): an exact version comes
/// from the feed into the packages folder with the SHA-512 NuGet recorded for it, <c>--offline</c>
/// makes no feed request at all, and a download, verification or extraction failure leaves no
/// content, no record and no staging behind.
/// </summary>
public sealed class PackageDownloadTests
{
    private static readonly DateTimeOffset Installed = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_exact_version_downloads_into_the_packages_folder_with_nugets_sha512()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.SkillsManifest).Skill("greek").Save();
        List<string> requests = [];

        PackageInstallResult result = await feed.Installer(requests.Add, now: Installed)
            .InstallAsync(Request(Path.Combine(temp.Root, "store")), Ct);

        Assert.Equal(PackageInstallStatus.Installed, result.Status);
        Assert.True(File.Exists(feed.CachedNupkg("Acme.Toolkit", "1.4.2")));
        Assert.Equal(feed.Sha512("Acme.Toolkit", "1.4.2"), result.Record.Package.Sha512);
        // The hash in the record is the one NuGet wrote beside the nupkg, not one Lunate invented.
        Assert.Equal(
            result.Record.Package.Sha512,
            File.ReadAllText(feed.CachedHash("Acme.Toolkit", "1.4.2")).Trim()
        );
        Assert.Contains(
            requests,
            request => request.Contains("download", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task An_offline_install_from_a_warm_packages_folder_makes_no_feed_request()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
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
        Assert.Equal(feed.Sha512("Acme.Toolkit", "1.4.2"), result.Record.Package.Sha512);
    }

    [Fact]
    public async Task An_offline_install_from_a_cold_packages_folder_fails_and_writes_nothing()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        List<string> requests = [];

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                feed.Installer(requests.Add, offline: true, now: Installed)
                    .InstallAsync(Request(store), Ct)
        );

        Assert.Contains("Acme.Toolkit", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(feed.GlobalPackages, refusal.Message, StringComparison.Ordinal);
        Assert.Empty(requests);
        NothingWritten(store, "acme.toolkit");
    }

    [Fact]
    public async Task A_cached_nupkg_that_does_not_match_nugets_hash_is_refused_and_writes_nothing()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        await feed.Installer(now: Installed).InstallAsync(Request(store), Ct);
        Directory.Delete(store, recursive: true);

        // A packages folder whose nupkg and recorded hash disagree: the bytes on disk are not the
        // bytes NuGet vouched for, so nothing may be installed from them.
        File.WriteAllText(feed.CachedHash("Acme.Toolkit", "1.4.2"), "bm90IHRoZSBoYXNo");

        InvalidDataException refusal = await Assert.ThrowsAsync<InvalidDataException>(() =>
            feed.Installer(offline: true, now: Installed).InstallAsync(Request(store), Ct)
        );

        Assert.Contains(
            feed.CachedNupkg("Acme.Toolkit", "1.4.2"),
            refusal.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            feed.CachedHash("Acme.Toolkit", "1.4.2"),
            refusal.Message,
            StringComparison.Ordinal
        );
        NothingWritten(store, "acme.toolkit");

        // Repairing the packages folder (here: restoring the hash NuGet wrote) is all a retry needs.
        File.WriteAllText(
            feed.CachedHash("Acme.Toolkit", "1.4.2"),
            feed.Sha512("Acme.Toolkit", "1.4.2")
        );
        PackageInstallResult retry = await feed.Installer(offline: true, now: Installed)
            .InstallAsync(Request(store), Ct);
        Assert.Equal(PackageInstallStatus.Installed, retry.Status);
    }

    [Fact]
    public async Task A_failed_download_writes_nothing_and_a_second_run_succeeds()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        string store = Path.Combine(temp.Root, "store");

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                feed.Installer(now: Installed).InstallAsync(Request(store), Ct)
        );

        Assert.Contains("Acme.Toolkit", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("1.4.2", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("local", refusal.Message, StringComparison.Ordinal);
        NothingWritten(store, "acme.toolkit");

        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        PackageInstallResult retry = await feed.Installer(now: Installed)
            .InstallAsync(Request(store), Ct);

        Assert.Equal(PackageInstallStatus.Installed, retry.Status);
        Assert.True(File.Exists(Path.Combine(store, "extensions", "acme-tools", "acme-tools.dll")));
    }

    [Fact]
    public async Task An_entry_that_cannot_be_extracted_writes_nothing_and_a_second_run_succeeds()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        string nupkg = feed.Bundle()
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .File("extension/plugins/acme-extra.dll", "corrupt me")
            .Save();
        string store = Path.Combine(temp.Root, "store");

        // A cached nupkg whose content entry is corrupt, with a hash that matches the corrupt bytes:
        // the package passes verification and fails while its content is read, which is the failure
        // the commit point has to survive.
        string cached = feed.CachedNupkg("Acme.Toolkit", "1.4.2");
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        TestPackage.CorruptEntry(nupkg, "extension/plugins/acme-extra.dll", cached);
        File.WriteAllText(feed.CachedHash("Acme.Toolkit", "1.4.2"), FileHashes.Sha512(cached));

        InvalidDataException refusal = await Assert.ThrowsAnyAsync<InvalidDataException>(() =>
            feed.Installer(offline: true, now: Installed).InstallAsync(Request(store), Ct)
        );

        Assert.Contains("acme-extra.dll", refusal.Message, StringComparison.Ordinal);
        NothingWritten(store, "acme.toolkit");

        // The same run against an intact nupkg completes: nothing from the failure blocks a retry.
        File.Copy(nupkg, cached, overwrite: true);
        File.WriteAllText(feed.CachedHash("Acme.Toolkit", "1.4.2"), FileHashes.Sha512(cached));
        PackageInstallResult retry = await feed.Installer(offline: true, now: Installed)
            .InstallAsync(Request(store), Ct);
        Assert.Equal(PackageInstallStatus.Installed, retry.Status);
    }

    /// <summary>
    /// A failed install leaves no trace: no record, no extension or skill content, no staging, and no
    /// package directory once it holds nothing.
    /// </summary>
    private static void NothingWritten(string store, string packageDirectory)
    {
        string directory = Path.Combine(store, "packages", packageDirectory);
        Assert.False(
            File.Exists(Path.Combine(directory, PackageInstallRecordJson.FileName)),
            "a failed install writes no record"
        );
        Assert.False(Directory.Exists(Path.Combine(store, "extensions")), "no extension content");
        Assert.False(Directory.Exists(Path.Combine(store, "skills")), "no skill content");
        Assert.False(
            Directory.Exists(directory)
                && Directory.EnumerateDirectories(directory, "staging-*").Any(),
            "no staging directory survives a failed install"
        );
        Assert.False(
            Directory.Exists(directory),
            "the package directory is gone when it holds nothing"
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

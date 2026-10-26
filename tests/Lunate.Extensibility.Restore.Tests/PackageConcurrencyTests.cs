namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// Two package operations that run at once (design D6): each one stages into a directory of its
/// own and holds the package's claim from the record it reads to the record it writes, so one
/// install cannot delete another's staging, and an install and an uninstall leave either the
/// installed state or the removed one — never half of each.
/// </summary>
public sealed class PackageConcurrencyTests
{
    private static readonly DateTimeOffset Installed = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_installs_of_one_package_at_once_produce_one_consistent_install()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        // Warm the shared packages folder, so the two runs below meet on the package's record and
        // content rather than on NuGet's own download of the same nupkg.
        await feed.Installer(now: Installed)
            .InstallAsync(Request(Path.Combine(temp.Root, "warm"), "Acme.Toolkit"), Ct);

        Task<PackageInstallResult> first = Task.Run(
            () => feed.Installer(now: Installed).InstallAsync(Request(store, "Acme.Toolkit"), Ct),
            Ct
        );
        Task<PackageInstallResult> second = Task.Run(
            () => feed.Installer(now: Installed).InstallAsync(Request(store, "Acme.Toolkit"), Ct),
            Ct
        );
        PackageInstallResult[] results = await Task.WhenAll(first, second);

        // One run installed and the other, waiting on the claim, found the record it had written.
        Assert.Single(results, result => result.Status == PackageInstallStatus.Installed);
        Assert.Single(results, result => result.Status == PackageInstallStatus.AlreadyInstalled);
        Assert.Single(results.Select(result => result.RecordPath).Distinct(StringComparer.Ordinal));

        AssertInstalledConsistently(store);
        Assert.Empty(Staging(store));
        Assert.True(
            File.Exists(Path.Combine(store, "packages", "locks", "acme.toolkit.lock")),
            "the claim is keyed by the canonical record path"
        );
    }

    [Fact]
    public async Task An_install_racing_an_uninstall_leaves_the_installed_state_or_the_removed_one()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        await feed.Installer(now: Installed).InstallAsync(Request(store, "Acme.Toolkit"), Ct);

        Task<PackageInstallResult> install = Task.Run(
            () => feed.Installer(now: Installed).InstallAsync(Request(store, "Acme.Toolkit"), Ct),
            Ct
        );
        Task<PackageUninstallResult> uninstall = Task.Run(
            () =>
                new PackageUninstaller().Uninstall(
                    new PackageUninstallRequest { Id = "Acme.Toolkit", StoreRoot = store }
                ),
            Ct
        );
        await Task.WhenAll(install, uninstall);

        // Whichever ran second acted on what the first left: the record it read was the record the
        // other one wrote, so the content and the record agree at the end.
        AssertInstalledOrRemoved(store);
        Assert.Empty(Staging(store));
    }

    [Fact]
    public void Each_operation_stages_in_a_directory_of_its_own()
    {
        PackageInstallTarget target = PackageInstallTarget.ForUser(
            Path.Combine(Path.GetTempPath(), "lunate-restore-tests", "store"),
            "Acme.Toolkit"
        );

        string first = target.NewStagingDirectory();
        string second = target.NewStagingDirectory();

        Assert.NotEqual(first, second);
        Assert.Equal(target.RecordDirectory, Path.GetDirectoryName(first));
        Assert.StartsWith("staging-", Path.GetFileName(first), StringComparison.Ordinal);
        // Named, not created: nothing exists until the run stages a file.
        Assert.False(Directory.Exists(first));
    }

    [Fact]
    public async Task A_staging_directory_a_stopped_run_left_behind_is_cleared_by_the_next_install()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        string leftover = Path.Combine(store, "packages", "acme.toolkit", "staging-leftover");
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "half.bin"), "half");

        await feed.Installer(now: Installed).InstallAsync(Request(store, "Acme.Toolkit"), Ct);

        Assert.False(
            Directory.Exists(leftover),
            "the stopped run's staging is cleared before staging"
        );
        Assert.Empty(Staging(store));
        AssertInstalledConsistently(store);
    }

    /// <summary>Every recorded path still answers for its hash, and the directory holds no staging.</summary>
    private static void AssertInstalledConsistently(string store)
    {
        string recordPath = Path.Combine(
            store,
            "packages",
            "acme.toolkit",
            PackageInstallRecordJson.FileName
        );
        PackageInstallRecord record =
            PackageInstallRecordJson.TryRead(recordPath, out string? problem)
            ?? throw new InvalidOperationException($"the record did not read: {problem}");

        Assert.Null(problem);
        Assert.True(PackageContentVerifier.Verify(record, store).Clean);
        Assert.True(File.Exists(Path.Combine(store, "extensions", "acme-tools", "acme-tools.dll")));
    }

    /// <summary>
    /// The installed state or the removed one: a record names content that is there, and no record
    /// means the content it named is gone. A mixture is the failure this proof is for.
    /// </summary>
    private static void AssertInstalledOrRemoved(string store)
    {
        string recordPath = Path.Combine(
            store,
            "packages",
            "acme.toolkit",
            PackageInstallRecordJson.FileName
        );
        if (File.Exists(recordPath))
        {
            AssertInstalledConsistently(store);
            return;
        }

        Assert.False(
            Directory.Exists(Path.Combine(store, "extensions", "acme-tools")),
            "the record is gone, so the extension it named is gone too"
        );
        Assert.False(
            Directory.Exists(Path.Combine(store, "skills", "greek")),
            "the record is gone, so the skill it named is gone too"
        );
    }

    /// <summary>The staging directories a run left behind; a finished run leaves none.</summary>
    private static string[] Staging(string store) =>
        Directory.Exists(Path.Combine(store, "packages", "acme.toolkit"))
            ?
            [
                .. Directory.EnumerateDirectories(
                    Path.Combine(store, "packages", "acme.toolkit"),
                    "staging-*"
                ),
            ]
            : [];

    private static PackageInstallRequest Request(string store, string id) =>
        new()
        {
            Id = id,
            Version = "1.4.2",
            Target = PackageInstallTarget.ForUser(store, id),
        };
}

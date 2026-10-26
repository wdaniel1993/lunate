namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// Real-path confinement (design D11): a recorded path is relative and lexically confined, but a
/// symlinked ancestor inside the scope root would still carry an install or an uninstall outside it.
/// Both refuse by naming the path, and neither writes nor deletes beyond the link.
/// </summary>
public sealed class PackageScopeSymlinkTests
{
    private static readonly DateTimeOffset Installed = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_install_through_a_symlinked_extensions_directory_is_refused_and_writes_nothing_outside()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        string worktree = Path.Combine(temp.Root, "worktree");
        string outside = Path.Combine(temp.Root, "outside");
        Directory.CreateDirectory(Path.Combine(worktree, ".lunate"));
        Directory.CreateDirectory(outside);
        string link = Path.Combine(worktree, ".lunate", "extensions");
        if (!TryLink(link, outside))
        {
            return;
        }

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                feed.Installer(now: Installed)
                    .InstallAsync(
                        new PackageInstallRequest
                        {
                            Id = "Acme.Toolkit",
                            Version = "1.4.2",
                            Target = PackageInstallTarget.ForProject(
                                worktree,
                                store,
                                "Acme.Toolkit"
                            ),
                        },
                        Ct
                    )
        );

        Assert.Contains(link, refused.Message, StringComparison.Ordinal);
        Assert.Contains("symbolic link", refused.Message, StringComparison.Ordinal);
        Assert.Contains("refusing to write", refused.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(outside));
        Assert.False(
            File.Exists(
                Path.Combine(store, "packages", "acme.toolkit", PackageInstallRecordJson.FileName)
            )
        );
    }

    [Fact]
    public async Task An_uninstall_through_a_symlinked_extension_directory_is_refused_and_deletes_nothing()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = Path.Combine(temp.Root, "store");
        PackageInstallResult install = await feed.Installer(now: Installed)
            .InstallAsync(
                new PackageInstallRequest
                {
                    Id = "Acme.Toolkit",
                    Version = "1.4.2",
                    Target = PackageInstallTarget.ForUser(store, "Acme.Toolkit"),
                },
                Ct
            );
        string extension = Path.Combine(store, "extensions", "acme-tools");
        string outside = Path.Combine(temp.Root, "outside");
        Directory.Move(extension, outside);
        string link = Path.Combine(store, "extensions", "acme-tools");
        if (!TryLink(link, outside))
        {
            return;
        }

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            new PackageUninstaller().Uninstall(
                new PackageUninstallRequest { Id = "Acme.Toolkit", StoreRoot = store }
            )
        );

        Assert.Contains("symbolic link", refused.Message, StringComparison.Ordinal);
        Assert.Contains("refusing to delete", refused.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(outside, "acme-tools.dll")));
        Assert.True(File.Exists(Path.Combine(outside, "extension.json")));
        Assert.True(File.Exists(install.RecordPath));
    }

    /// <summary>Links, or skips the test where the platform or CI does not allow creating one.</summary>
    private static bool TryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or PlatformNotSupportedException
            )
        {
            Assert.Skip("Symbolic links are not available in this environment.");
            return false;
        }
    }
}

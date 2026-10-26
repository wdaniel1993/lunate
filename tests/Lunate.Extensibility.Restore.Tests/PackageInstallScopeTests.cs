namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// Scope and collisions (spec: "Where installed content lands", "Scope, precedence and
/// collisions"): a project install writes the worktree's scope and nothing in the state root's
/// skills, a duplicate extension id is refused at install naming both providers and the uninstall
/// that clears one, an existing skill directory is never written into, and a same-named skill in
/// the other scope warns once and lets the existing shadowing rule decide.
/// </summary>
public sealed class PackageInstallScopeTests
{
    private static readonly DateTimeOffset Installed = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_project_install_writes_only_the_worktree_scope()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = temp.Path("store");
        string worktree = temp.Path("worktree");
        Directory.CreateDirectory(worktree);

        await feed.Installer(now: Installed)
            .InstallAsync(
                new PackageInstallRequest
                {
                    Id = "Acme.Toolkit",
                    Version = "1.4.2",
                    Target = PackageInstallTarget.ForProject(worktree, store, "Acme.Toolkit"),
                },
                Ct
            );

        Assert.True(
            File.Exists(
                Path.Combine(worktree, ".lunate", "extensions", "acme-tools", "acme-tools.dll")
            )
        );
        Assert.True(File.Exists(Path.Combine(worktree, ".agents", "skills", "greek", "SKILL.md")));
        Assert.False(
            Directory.Exists(Path.Combine(store, "skills")),
            "a project install writes nothing to the state root's skills"
        );
        Assert.False(
            Directory.Exists(Path.Combine(store, "extensions")),
            "a project install writes nothing to the state root's extensions"
        );
    }

    [Fact]
    public async Task A_duplicate_extension_id_from_another_install_is_refused()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        feed.Bundle(id: "Beta.Toolkit", version: "1.0.0")
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .Save();
        string store = temp.Path("store");
        PackageInstaller installer = feed.Installer(now: Installed);
        PackageInstallResult first = await installer.InstallAsync(
            Request(store, "Acme.Toolkit", "1.4.2"),
            Ct
        );

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                installer.InstallAsync(Request(store, "Beta.Toolkit", "1.0.0"), Ct)
        );

        Assert.Contains("Beta.Toolkit", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("acme-tools", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Acme.Toolkit", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(first.RecordPath, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("lunate uninstall Acme.Toolkit", refusal.Message, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(store, "packages", "beta.toolkit", "install.json")),
            "a refused install writes no record"
        );
        Assert.Equal(
            ["acme-tools.dll", "extension.json"],
            Directory
                .GetFiles(Path.Combine(store, "extensions", "acme-tools"))
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    public async Task A_duplicate_extension_id_against_a_hand_placed_extension_is_refused()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = temp.Path("store");
        string handPlaced = Path.Combine(store, "extensions", "acme-tools");
        Directory.CreateDirectory(handPlaced);
        File.WriteAllText(
            Path.Combine(handPlaced, "extension.json"),
            """
            {"id":"acme-tools","version":"9.9.9","apiVersion":"^1.0.0","entryAssembly":"acme-tools.dll"}
            """
        );

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                feed.Installer(now: Installed)
                    .InstallAsync(Request(store, "Acme.Toolkit", "1.4.2"), Ct)
        );

        Assert.Contains(handPlaced, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("acme-tools", refusal.Message, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(store, "packages", "acme.toolkit", "install.json")),
            "a refused install writes no record"
        );
    }

    [Fact]
    public async Task A_duplicate_extension_id_against_a_single_file_extension_is_refused()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.FullManifest).Extension().Skill("greek").Save();
        string store = temp.Path("store");
        string extensions = Path.Combine(store, "extensions");
        Directory.CreateDirectory(extensions);
        string file = Path.Combine(extensions, "acme-single.cs");
        File.WriteAllText(
            file,
            """
            #:extension acme-tools@2.0.0
            public sealed class Entry : Lunate.Extensibility.Abstractions.IExtensionFactory { }
            """
        );

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                feed.Installer(now: Installed)
                    .InstallAsync(Request(store, "Acme.Toolkit", "1.4.2"), Ct)
        );

        Assert.Contains(file, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("acme-tools", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "install.json",
            string.Join(
                Environment.NewLine,
                Directory.Exists(Path.Combine(store, "packages"))
                    ? Directory.GetFiles(
                        Path.Combine(store, "packages"),
                        "*",
                        SearchOption.AllDirectories
                    )
                    : []
            ),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task An_existing_skill_directory_is_refused_and_left_unchanged()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.SkillsManifest).Skill("review").Save();
        string store = temp.Path("store");
        string existing = Path.Combine(store, "skills", "review");
        Directory.CreateDirectory(existing);
        string skillFile = Path.Combine(existing, "SKILL.md");
        File.WriteAllText(skillFile, "# hand-written");

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                feed.Installer(now: Installed)
                    .InstallAsync(Request(store, "Acme.Toolkit", "1.4.2"), Ct)
        );

        Assert.Contains(existing, refusal.Message, StringComparison.Ordinal);
        Assert.Equal("# hand-written", File.ReadAllText(skillFile));
        Assert.False(File.Exists(Path.Combine(store, "packages", "acme.toolkit", "install.json")));
    }

    [Fact]
    public async Task A_same_named_skill_in_the_other_scope_warns_once()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.SkillsManifest).Skill("review").Save();
        string store = temp.Path("store");
        string worktree = temp.Path("worktree");
        string projectSkill = Path.Combine(worktree, ".agents", "skills", "review");
        Directory.CreateDirectory(projectSkill);
        File.WriteAllText(Path.Combine(projectSkill, "SKILL.md"), "# project review");

        PackageInstallResult result = await feed.Installer(now: Installed)
            .InstallAsync(
                new PackageInstallRequest
                {
                    Id = "Acme.Toolkit",
                    Version = "1.4.2",
                    Target = PackageInstallTarget.ForUser(store, "Acme.Toolkit"),
                    Worktree = worktree,
                },
                Ct
            );

        Assert.Equal(PackageInstallStatus.Installed, result.Status);
        Assert.True(File.Exists(Path.Combine(store, "skills", "review", "SKILL.md")));
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("review", warning, StringComparison.Ordinal);
        Assert.Contains(projectSkill, warning, StringComparison.Ordinal);
        Assert.Contains("project", warning, StringComparison.Ordinal);
        Assert.Equal("# project review", File.ReadAllText(Path.Combine(projectSkill, "SKILL.md")));
    }

    [Fact]
    public async Task The_same_package_cannot_be_installed_into_the_other_scope()
    {
        using var temp = new TempDirectory();
        var feed = new BundleFeed(temp.Root);
        feed.Bundle().Manifest(TestPackage.SkillsManifest).Skill("greek").Save();
        string store = temp.Path("store");
        string worktree = temp.Path("worktree");
        Directory.CreateDirectory(worktree);
        PackageInstaller installer = feed.Installer(now: Installed);
        await installer.InstallAsync(Request(store, "Acme.Toolkit", "1.4.2"), Ct);

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                installer.InstallAsync(
                    new PackageInstallRequest
                    {
                        Id = "Acme.Toolkit",
                        Version = "1.4.2",
                        Target = PackageInstallTarget.ForProject(worktree, store, "Acme.Toolkit"),
                    },
                    Ct
                )
        );

        Assert.Contains(
            "already installed for the user scope",
            refusal.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("lunate uninstall Acme.Toolkit", refusal.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(worktree, ".agents")));
    }

    private static PackageInstallRequest Request(string store, string id, string version) =>
        new()
        {
            Id = id,
            Version = version,
            Target = PackageInstallTarget.ForUser(store, id),
        };
}

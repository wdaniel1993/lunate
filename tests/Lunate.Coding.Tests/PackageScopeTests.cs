using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// Scope at the surface a session uses (spec: "Where installed content lands", "Scope, precedence
/// and collisions"): a project install belongs to its worktree, so another one lists none of it and
/// discovers none of its content; <c>lunate cache prune</c> leaves an install alone; and a skill the
/// other scope already provides warns once and lets the existing shadowing rule decide.
/// </summary>
public sealed class PackageScopeTests
{
    [Fact]
    public void A_project_install_is_invisible_from_another_worktree()
    {
        using var feed = new PackageFeedFixture();
        GitTestRepo.Init(feed.Worktree);
        feed.Bundle(skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2", "--project"], out _, out _));
        string other = Path.Combine(feed.Home, "..", "other-worktree");
        other = Path.GetFullPath(other);
        Directory.CreateDirectory(other);
        GitTestRepo.Init(other);

        StringWriter output = new();
        StringWriter errors = new();
        int exit = PackagesCommand.Run(
            [],
            other,
            output,
            errors,
            new ExtensionSeams { StorePath = feed.Store }
        );

        Assert.Equal(PackagesCommand.ExitOk, exit);
        Assert.Equal("packages: none installed\n", output.ToString().ReplaceLineEndings("\n"));

        var loader = new ExtensionLoader(new ExtensionHostOptions { StorePath = feed.Store });
        loader.Discover(other, other);
        Assert.Empty(loader.Descriptors);

        SkillScanResult scan = SkillScanner.Scan(new SkillScanRequest(other, other, feed.Home, []));
        Assert.DoesNotContain(scan.Skills, skill => skill.Name == "greek");
    }

    [Fact]
    public void Cache_prune_leaves_an_installed_package_alone()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out _));

        StringWriter output = new();
        StringWriter errors = new();
        int exit = CacheCommand.Run(
            ["prune"],
            feed.Worktree,
            output,
            errors,
            new ExtensionSeams { StorePath = feed.Store }
        );

        Assert.Equal(CacheCommand.ExitOk, exit);
        Assert.Empty(errors.ToString());
        Assert.False(
            Directory.Exists(
                Path.Combine(feed.Store, "cache", "extensions", "acme-tools", "builds")
            ),
            "an install writes no build"
        );
        Assert.True(
            File.Exists(Path.Combine(feed.Store, "extensions", "acme-tools", "acme-tools.dll"))
        );
        Assert.True(File.Exists(Path.Combine(feed.Store, "skills", "greek", "SKILL.md")));
        Assert.True(
            File.Exists(Path.Combine(feed.Store, "packages", "acme.toolkit", "install.json"))
        );
    }

    [Fact]
    public void A_shadowed_skill_warns_once_and_the_install_still_succeeds()
    {
        using var feed = new PackageFeedFixture();
        GitTestRepo.Init(feed.Worktree);
        string projectSkill = Path.Combine(feed.Worktree, ".agents", "skills", "review");
        Directory.CreateDirectory(projectSkill);
        File.WriteAllText(Path.Combine(projectSkill, "SKILL.md"), "# project review");
        feed.Bundle(skills: ["review"]);

        int exit = feed.Run(["Acme.Toolkit@1.4.2"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Contains("installed:", output, StringComparison.Ordinal);
        string warning = Assert.Single(
            errors.TrimEnd('\n').Split('\n'),
            line => line.Contains("review", StringComparison.Ordinal)
        );
        Assert.StartsWith("lunate: ", warning, StringComparison.Ordinal);
        Assert.Contains(projectSkill, warning, StringComparison.Ordinal);
        Assert.Contains("project wins over user", warning, StringComparison.Ordinal);
        Assert.Equal("# project review", File.ReadAllText(Path.Combine(projectSkill, "SKILL.md")));
    }
}

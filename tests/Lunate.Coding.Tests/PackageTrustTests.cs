using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// A project install in the trust machinery (spec: "Install runs no package code and records no
/// trust", "Project extension trust", "Project skills trust"): the pending decisions name the source
/// package on one line, <c>lunate trust</c> records them without prompting, a later session asks
/// nothing, and <c>/skills</c> shows the package a skill came from.
/// </summary>
public sealed class PackageTrustTests
{
    [Fact]
    public void A_project_install_shows_up_in_the_trust_inventory_with_its_package()
    {
        using var feed = new PackageFeedFixture();
        GitTestRepo.Init(feed.Worktree);
        feed.Bundle(skills: ["greek", "roman"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2", "--project"], out _, out _));

        Result trust = Trust(feed, []);

        Assert.Equal(TrustCommand.ExitPending, trust.ExitCode);
        Assert.False(
            File.Exists(Path.Combine(feed.Store, "trust.json")),
            "an install writes no trust record"
        );
        Assert.Contains(
            $"Load project extension 'acme-tools' 0.1.0 from",
            trust.Output,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "  from package Acme.Toolkit@1.4.2 (local)",
            trust.Output,
            StringComparison.Ordinal
        );
        Assert.Contains("skills: greek, roman", trust.Output, StringComparison.Ordinal);

        // One line per pending decision: the extension and the skills both name the package.
        Assert.Equal(2, Occurrences(trust.Output, "from package Acme.Toolkit@1.4.2 (local)"));
    }

    [Fact]
    public void Approving_records_them_and_a_later_session_asks_nothing()
    {
        using var feed = new PackageFeedFixture();
        GitTestRepo.Init(feed.Worktree);
        feed.Bundle(skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2", "--project"], out _, out _));
        Assert.Equal(TrustCommand.ExitPending, Trust(feed, []).ExitCode);

        Result approved = Trust(feed, ["--yes"]);

        Assert.Equal(TrustCommand.ExitOk, approved.ExitCode);
        Assert.Contains("trust: recorded", approved.Output, StringComparison.Ordinal);
        Assert.Equal(TrustCommand.ExitOk, Trust(feed, []).ExitCode);
        Assert.Contains("trust: nothing pending", Trust(feed, []).Output, StringComparison.Ordinal);

        // What a session's inventory asks: nothing.
        TrustInventory inventory = TrustInventory.Create(
            new Workspace(feed.Worktree),
            new ExtensionSeams { StorePath = feed.Store }
        );
        Assert.False(inventory.HasPending);
    }

    [Fact]
    public void Slash_skills_shows_the_package_a_skill_came_from()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out _));

        SkillScanResult scan = SkillScanner.Scan(
            new SkillScanRequest(feed.Worktree, RepoRoot: null, feed.Home, [])
        );
        SkillDescriptor greek = Assert.Single(scan.Skills, skill => skill.Name == "greek");
        Assert.Equal("Acme.Toolkit@1.4.2", greek.FromPackage);
        Assert.Contains(
            "greek (user, from Acme.Toolkit@1.4.2) — The greek skill.",
            SkillsView.Text(scan.Skills),
            StringComparison.Ordinal
        );

        // The user install wrote the state root and nothing beside it in the profile.
        Assert.False(Directory.Exists(Path.Combine(feed.Home, ".agents")));
    }

    [Fact]
    public void A_user_install_is_trusted_by_installing_and_asks_nothing()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out _));

        Result trust = Trust(feed, []);

        Assert.Equal(TrustCommand.ExitOk, trust.ExitCode);
        Assert.Contains(
            "trust: no records for this repository",
            trust.Output,
            StringComparison.Ordinal
        );
        Assert.Contains("trust: nothing pending", trust.Output, StringComparison.Ordinal);
    }

    private static Result Trust(PackageFeedFixture feed, string[] args)
    {
        StringWriter output = new();
        StringWriter errors = new();
        int exitCode = TrustCommand.Run(
            args,
            feed.Worktree,
            output,
            errors,
            new ExtensionSeams { StorePath = feed.Store }
        );
        return new Result(
            exitCode,
            output.ToString().ReplaceLineEndings("\n"),
            errors.ToString().ReplaceLineEndings("\n")
        );
    }

    private static int Occurrences(string text, string value)
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

    private sealed record Result(int ExitCode, string Output, string Errors);
}

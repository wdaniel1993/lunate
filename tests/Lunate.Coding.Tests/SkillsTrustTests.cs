using System.Text.Json;
using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// Project-skills trust: the trust command's inspect/approve/clear paths (the non-interactive
/// parity) and the changed-content re-prompt through the real store.
/// </summary>
public sealed class SkillsTrustTests
{
    private static void WriteSkill(string directory, string name, string description = "D")
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"---\nname: {name}\ndescription: {description}\n---\n\nBody\n"
        );
    }

    private static (string Worktree, string Store) ProjectWithSkills(
        TempDirectory temp,
        params string[] names
    )
    {
        string worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        foreach (string name in names)
        {
            WriteSkill(Path.Combine(worktree, ".agents", "skills", name), name);
        }

        return (worktree, temp.File("store"));
    }

    [Fact]
    public void Trust_inspect_lists_the_skills_question_and_reports_pending()
    {
        using var temp = new TempDirectory();
        (string worktree, string store) = ProjectWithSkills(temp, "roman", "greek");
        using var output = new StringWriter();

        int exitCode = Cli.Run(
            ["trust"],
            output,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store },
            workingDirectory: worktree
        );

        Assert.Equal(TrustCommand.ExitPending, exitCode);
        string report = output.ToString();
        Assert.Contains(
            "Load project skills from this repository",
            report,
            StringComparison.Ordinal
        );
        Assert.Contains("skills: greek, roman", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Trust_yes_records_the_skills_decision_and_a_later_inspect_is_clean()
    {
        using var temp = new TempDirectory();
        (string worktree, string store) = ProjectWithSkills(temp, "greek");
        var seams = new ExtensionSeams { StorePath = store };
        using var output = new StringWriter();

        int approved = Cli.Run(
            ["trust", "--yes"],
            output,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: seams,
            workingDirectory: worktree,
            stdinIsTerminal: true
        );
        using var inspected = new StringWriter();
        int later = Cli.Run(
            ["trust"],
            inspected,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: seams,
            workingDirectory: worktree
        );

        Assert.Equal(TrustCommand.ExitOk, approved);
        Assert.Contains("trust: recorded skills — ", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(TrustCommand.ExitOk, later);
        Assert.Contains("skills — ", inspected.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Trust_json_reports_the_skills_pending_entry()
    {
        using var temp = new TempDirectory();
        (string worktree, string store) = ProjectWithSkills(temp, "greek");
        using var output = new StringWriter();

        int exitCode = Cli.Run(
            ["trust", "--json"],
            output,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store },
            workingDirectory: worktree
        );

        Assert.Equal(TrustCommand.ExitPending, exitCode);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement pending = Assert.Single(
            document.RootElement.GetProperty("pending").EnumerateArray(),
            node => node.GetProperty("kind").GetString() == "skills"
        );
        Assert.Equal(
            "greek",
            Assert.Single(pending.GetProperty("names").EnumerateArray()).GetString()
        );
    }

    [Fact]
    public void Changed_skill_content_re_prompts()
    {
        using var temp = new TempDirectory();
        (string worktree, string store) = ProjectWithSkills(temp, "greek");
        var seams = new ExtensionSeams { StorePath = store };
        int approved = Cli.Run(
            ["trust", "--yes"],
            TextWriter.Null,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: seams,
            workingDirectory: worktree,
            stdinIsTerminal: true
        );
        Assert.Equal(TrustCommand.ExitOk, approved);

        WriteSkill(Path.Combine(worktree, ".agents", "skills", "greek"), "greek", "Changed");

        int again = Cli.Run(
            ["trust"],
            TextWriter.Null,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: seams,
            workingDirectory: worktree
        );

        Assert.Equal(TrustCommand.ExitPending, again);
    }

    [Fact]
    public void Trust_clear_removes_the_skills_record_and_a_new_session_asks_again()
    {
        using var temp = new TempDirectory();
        (string worktree, string store) = ProjectWithSkills(temp, "greek");
        var seams = new ExtensionSeams { StorePath = store };
        Assert.Equal(
            TrustCommand.ExitOk,
            Cli.Run(
                ["trust", "--yes"],
                TextWriter.Null,
                TextWriter.Null,
                TestContext.Current.CancellationToken,
                extensions: seams,
                workingDirectory: worktree,
                stdinIsTerminal: true
            )
        );

        using var cleared = new StringWriter();
        int clear = Cli.Run(
            ["trust", "--clear"],
            cleared,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: seams,
            workingDirectory: worktree
        );

        Assert.Equal(TrustCommand.ExitOk, clear);
        Assert.Contains("skills — ", cleared.ToString(), StringComparison.Ordinal);
        int inspected = Cli.Run(
            ["trust"],
            TextWriter.Null,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: seams,
            workingDirectory: worktree
        );
        Assert.Equal(TrustCommand.ExitPending, inspected);
    }
}

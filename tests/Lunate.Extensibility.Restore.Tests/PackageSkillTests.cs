namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// The skills a package carries (spec: "A package's layout"): one skill per immediate subdirectory,
/// its <c>SKILL.md</c> read by the existing frontmatter parser, its name equal to the directory, and
/// one installable skill per name.
/// </summary>
public sealed class PackageSkillTests
{
    [Fact]
    public void Each_skill_directory_is_read_with_the_existing_frontmatter_parser()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"skills":"skills"}""")
            .Skill("roman")
            .Skill("greek")
            .Save();

        PackageInfo info = PackageReader.Read(nupkg);

        Assert.Equal(
            ["greek", "roman"],
            [.. info.Skills.Select(skill => skill.Name).Order(StringComparer.Ordinal)]
        );
        PackageSkill greek = Assert.Single(info.Skills, skill => skill.Name == "greek");
        Assert.Equal("skills/greek", greek.Directory);
        Assert.Equal("The greek skill.", greek.Description);
    }

    [Fact]
    public void A_skill_directory_without_a_SKILL_md_is_refused_naming_it()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"skills":"skills"}""")
            .File("skills/greek/notes.md", "no frontmatter here")
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("'skills/greek'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("SKILL.md", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_frontmatter_is_refused_with_the_reason()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"skills":"skills"}""")
            .File("skills/greek/SKILL.md", "---\nname: greek\n---\n\nNo description.\n")
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("'greek'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("missing a 'description'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_skill_whose_name_differs_from_its_directory_is_refused_naming_both()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"skills":"skills"}""")
            .Skill("greek", name: "roman")
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("'greek'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'roman'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_skills_with_one_name_are_refused_naming_both()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"skills":"skills"}""")
            .Skill("greek")
            .Skill("Greek")
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("skills/Greek", failure.Message, StringComparison.Ordinal);
        Assert.Contains("skills/greek", failure.Message, StringComparison.Ordinal);
        Assert.Contains("more than once", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_skills_folder_with_no_skill_directory_is_refused_naming_it()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"skills":"skills"}""")
            .File("skills/README.md", "skills of the acme toolkit")
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("'skills'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("no skill directories", failure.Message, StringComparison.Ordinal);
    }
}

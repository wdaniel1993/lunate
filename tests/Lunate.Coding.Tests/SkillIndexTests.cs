namespace Lunate.Coding.Tests;

public sealed class SkillIndexTests
{
    private static SkillDescriptor Skill(
        TempDirectory temp,
        string name,
        string description = "Does a thing",
        SkillScope scope = SkillScope.Project,
        string? extensionId = null,
        bool commandOnly = false,
        int depth = 0
    )
    {
        string directory = Path.Combine(
            temp.File("skills-store"),
            extensionId ?? scope.ToString(),
            name
        );
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "SKILL.md");
        File.WriteAllText(file, $"---\nname: {name}\ndescription: {description}\n---\nBody\n");
        return new SkillDescriptor(
            name,
            description,
            "Body",
            directory,
            file,
            scope,
            extensionId,
            depth,
            0,
            commandOnly,
            Valid: true,
            InvalidReason: null
        );
    }

    [Fact]
    public void The_index_carries_a_lead_line_and_one_line_per_skill()
    {
        using var temp = new TempDirectory();

        SkillIndex.Composed index = SkillIndex.Compose(
            [Skill(temp, "greek", "Use for Greek")],
            temp.Root,
            2000
        );

        Assert.Contains("read tool", index.Text, StringComparison.Ordinal);
        Assert.Contains("- greek: Use for Greek [", index.Text, StringComparison.Ordinal);
        Assert.Contains("SKILL.md]", index.Text, StringComparison.Ordinal);
        Assert.Equal(1, index.IndexedCount);
        Assert.Empty(index.NotIndexed);
    }

    [Fact]
    public void The_index_order_is_project_then_user_then_extension_by_name()
    {
        using var temp = new TempDirectory();
        List<SkillDescriptor> skills =
        [
            Skill(temp, "zeta", scope: SkillScope.Extension, extensionId: "hello"),
            Skill(temp, "alpha", scope: SkillScope.User),
            Skill(temp, "roman"),
            Skill(temp, "greek"),
            Skill(temp, "beta", scope: SkillScope.Extension, extensionId: "hello"),
        ];

        SkillIndex.Composed index = SkillIndex.Compose(skills, temp.Root, 2000);

        int greek = index.Text.IndexOf("- greek:", StringComparison.Ordinal);
        int roman = index.Text.IndexOf("- roman:", StringComparison.Ordinal);
        int alpha = index.Text.IndexOf("- alpha:", StringComparison.Ordinal);
        int beta = index.Text.IndexOf("- beta:", StringComparison.Ordinal);
        int zeta = index.Text.IndexOf("- zeta:", StringComparison.Ordinal);
        Assert.True(greek < roman && roman < alpha && alpha < beta && beta < zeta);
    }

    [Fact]
    public void Descriptions_are_truncated_at_200_characters()
    {
        using var temp = new TempDirectory();
        string description = new('x', 1000);

        SkillIndex.Composed index = SkillIndex.Compose(
            [Skill(temp, "long", description)],
            temp.Root,
            2000
        );

        int start = index.Text.IndexOf("long: ", StringComparison.Ordinal) + "long: ".Length;
        int end = index.Text.IndexOf(" [", start, StringComparison.Ordinal);
        string carried = index.Text[start..end];
        Assert.Equal(201, carried.Length);
        Assert.EndsWith("…", carried, StringComparison.Ordinal);
    }

    [Fact]
    public void A_description_cut_never_splits_a_surrogate_pair()
    {
        using var temp = new TempDirectory();
        string description = new string('a', 199) + "\U0001F600" + new string('b', 40);

        SkillIndex.Composed index = SkillIndex.Compose(
            [Skill(temp, "greek", description)],
            temp.Root,
            2000
        );

        int start = index.Text.IndexOf("greek: ", StringComparison.Ordinal) + "greek: ".Length;
        int end = index.Text.IndexOf(" [", start, StringComparison.Ordinal);
        string carried = index.Text[start..end];
        Assert.EndsWith("a…", carried, StringComparison.Ordinal);
        Assert.DoesNotContain(carried, char.IsSurrogate);
    }

    [Fact]
    public void Command_only_skills_stay_out_of_the_index()
    {
        using var temp = new TempDirectory();

        SkillIndex.Composed index = SkillIndex.Compose(
            [Skill(temp, "greek", commandOnly: true)],
            temp.Root,
            2000
        );

        Assert.Equal(string.Empty, index.Text);
        Assert.Equal(0, index.IndexedCount);
        Assert.Empty(index.NotIndexed);
    }

    [Fact]
    public void An_oversized_index_cuts_from_the_end_with_a_names_only_footer()
    {
        using var temp = new TempDirectory();
        string description = new('d', 200);
        List<SkillDescriptor> skills =
        [
            .. Enumerable
                .Range(0, 100)
                .Select(index => Skill(temp, $"skill-{index:D3}", description)),
        ];

        SkillIndex.Composed index = SkillIndex.Compose(skills, temp.Root, 2000);

        Assert.True(
            index.EstimatedTokens <= SkillIndex.MaxEstimatedTokens,
            $"estimated {index.EstimatedTokens} tokens"
        );
        Assert.True(index.IndexedCount < skills.Count);
        Assert.NotEmpty(index.NotIndexed);
        Assert.Contains(
            $"+{index.NotIndexed.Count} more skills (not indexed): skill-",
            index.Text,
            StringComparison.Ordinal
        );
        // The footer carries names only: no descriptions, no paths.
        string footer = index.Text[index.Text.LastIndexOf('\n')..];
        Assert.DoesNotContain("ddd", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("SKILL.md", footer, StringComparison.Ordinal);
        // The cut is deterministic and strictly from the end.
        string lastKept = $"- skill-{index.IndexedCount - 1:D3}:";
        Assert.Contains(lastKept, index.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"- skill-{index.IndexedCount:D3}:",
            index.Text,
            StringComparison.Ordinal
        );
        SkillIndex.Composed again = SkillIndex.Compose(skills, temp.Root, 2000);
        Assert.Equal(index.Text, again.Text);
    }

    [Fact]
    public void The_overflow_footer_truncates_the_name_list_at_400_characters()
    {
        using var temp = new TempDirectory();
        string description = new('d', 200);
        List<SkillDescriptor> skills =
        [
            .. Enumerable
                .Range(0, 100)
                .Select(index => Skill(temp, $"a-very-long-skill-name-{index:D3}", description)),
        ];

        SkillIndex.Composed index = SkillIndex.Compose(skills, temp.Root, 2000);

        int footer = index.Text.LastIndexOf('+');
        string names = index.Text[(index.Text.IndexOf(':', footer) + 2)..];
        Assert.True(names.Length <= SkillIndex.MaxOverflowNamesLength + 1, $"names {names.Length}");
        Assert.EndsWith("…", names, StringComparison.Ordinal);
    }

    [Fact]
    public void Extension_lines_over_the_per_extension_budget_drop_and_log()
    {
        using var temp = new TempDirectory();
        string description = new('d', 200);
        List<SkillDescriptor> skills =
        [
            Skill(temp, "greek"),
            .. Enumerable
                .Range(0, 30)
                .Select(index =>
                    Skill(temp, $"ext-{index:D2}", description, SkillScope.Extension, "hello")
                ),
        ];
        List<string> logs = [];

        SkillIndex.Composed index = SkillIndex.Compose(skills, temp.Root, 100, logs.Add);

        Assert.Contains("- greek:", index.Text, StringComparison.Ordinal);
        Assert.Contains("- ext-00:", index.Text, StringComparison.Ordinal);
        Assert.True(index.NotIndexed.Count > 0);
        Assert.Contains(
            logs,
            entry =>
                entry.Contains("extension 'hello'", StringComparison.Ordinal)
                && entry.Contains("100", StringComparison.Ordinal)
        );
    }
}

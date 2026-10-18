namespace Lunate.Coding.Tests;

public sealed class SkillScannerTests
{
    private static void WriteSkill(string directory, string name, string description = "D")
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"---\nname: {name}\ndescription: {description}\n---\n\nBody of {name}.\n"
        );
    }

    private static SkillScanRequest Request(
        TempDirectory temp,
        string? worktree = null,
        string? repoRoot = null,
        IReadOnlyList<ExtensionSkillSource>? extensions = null
    ) => new(worktree ?? temp.Root, repoRoot, temp.File("home"), extensions ?? []);

    [Fact]
    public void A_skill_at_the_repository_root_is_found_from_a_subdirectory()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string nested = Path.Combine(repo, "src", "inner");
        Directory.CreateDirectory(nested);
        WriteSkill(Path.Combine(repo, ".agents", "skills", "greek"), "greek");

        SkillScanResult result = SkillScanner.Scan(Request(temp, worktree: nested, repoRoot: repo));

        SkillDescriptor skill = Assert.Single(result.Skills);
        Assert.Equal("greek", skill.Name);
        Assert.Equal(SkillScope.Project, skill.Scope);
        Assert.Equal("Body of greek.", skill.Body);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Nothing_above_the_repository_root_is_read()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        WriteSkill(Path.Combine(temp.Root, ".agents", "skills", "outside"), "outside");

        SkillScanResult result = SkillScanner.Scan(Request(temp, worktree: repo, repoRoot: repo));

        Assert.Empty(result.Skills);
    }

    [Fact]
    public void A_linked_worktrees_own_skill_is_discovered()
    {
        using var temp = new TempDirectory();
        (string repository, string worktree) = LinkedWorktree(temp);
        WriteSkill(Path.Combine(worktree, ".agents", "skills", "greek"), "greek");

        SkillScanResult result = SkillScanner.Scan(
            Request(temp, worktree: worktree, repoRoot: repository)
        );

        SkillDescriptor skill = Assert.Single(result.Skills);
        Assert.Equal("greek", skill.Name);
        Assert.Equal(SkillScope.Project, skill.Scope);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void The_main_checkouts_skills_are_not_discovered_from_a_linked_worktree()
    {
        using var temp = new TempDirectory();
        (string repository, string worktree) = LinkedWorktree(temp);
        WriteSkill(Path.Combine(repository, ".agents", "skills", "main-only"), "main-only");

        SkillScanResult result = SkillScanner.Scan(
            Request(temp, worktree: worktree, repoRoot: repository)
        );

        Assert.Empty(result.Skills);
    }

    [Fact]
    public void Nothing_above_a_linked_worktrees_root_is_read()
    {
        using var temp = new TempDirectory();
        (string repository, string worktree) = LinkedWorktree(temp);
        string above = Path.Combine(temp.Root, ".agents", "skills", "above");
        WriteSkill(above, "above");

        SkillScanResult result = SkillScanner.Scan(
            Request(temp, worktree: worktree, repoRoot: repository)
        );

        // A walk escaping the worktree root would reach 'above', fail the boundary check and warn
        // about skipping it; the stop at the worktree root never looks there.
        Assert.Empty(result.Skills);
        Assert.DoesNotContain(
            result.Warnings,
            warning => warning.Contains(above, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void A_repository_root_that_is_not_an_ancestor_does_not_extend_the_walk()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string above = Path.Combine(temp.Root, ".agents", "skills", "above");
        WriteSkill(above, "above");

        SkillScanResult result = SkillScanner.Scan(
            Request(temp, worktree: worktree, repoRoot: temp.File("elsewhere"))
        );

        Assert.Empty(result.Skills);
        Assert.DoesNotContain(
            result.Warnings,
            warning => warning.Contains(above, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void A_project_skill_shadows_a_same_name_user_skill_with_one_warning()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        WriteSkill(Path.Combine(repo, ".agents", "skills", "greek"), "greek");
        WriteSkill(Path.Combine(temp.File("home"), ".lunate", "skills", "greek"), "greek");

        SkillScanResult result = SkillScanner.Scan(Request(temp, worktree: repo, repoRoot: repo));

        SkillDescriptor skill = Assert.Single(result.Skills);
        Assert.Equal(SkillScope.Project, skill.Scope);
        Assert.Equal("user", skill.Shadows);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("shadows", warning, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(temp.File("home"), ".lunate", "skills", "greek"), warning);
    }

    [Fact]
    public void Lunate_wins_over_agents_at_the_same_level()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        WriteSkill(Path.Combine(repo, ".agents", "skills", "greek"), "greek", "from agents");
        WriteSkill(Path.Combine(repo, ".lunate", "skills", "greek"), "greek", "from lunate");

        SkillScanResult result = SkillScanner.Scan(Request(temp, worktree: repo, repoRoot: repo));

        SkillDescriptor skill = Assert.Single(result.Skills);
        Assert.Equal("from lunate", skill.Description);
        Assert.Contains(".lunate", skill.Directory, StringComparison.Ordinal);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void The_nearest_level_wins_over_the_repository_root()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string nested = Path.Combine(repo, "src");
        Directory.CreateDirectory(nested);
        WriteSkill(Path.Combine(repo, ".agents", "skills", "greek"), "greek", "root copy");
        WriteSkill(Path.Combine(nested, ".agents", "skills", "greek"), "greek", "nested copy");

        SkillScanResult result = SkillScanner.Scan(Request(temp, worktree: nested, repoRoot: repo));

        SkillDescriptor skill = Assert.Single(result.Skills);
        Assert.Equal("nested copy", skill.Description);
    }

    [Fact]
    public void A_malformed_skill_warns_and_does_not_load()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string directory = Path.Combine(repo, ".agents", "skills", "broken");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), "---\nname: broken\n---\n");

        SkillScanResult result = SkillScanner.Scan(Request(temp, worktree: repo, repoRoot: repo));

        SkillDescriptor skill = Assert.Single(result.Skills);
        Assert.False(skill.Valid);
        Assert.Contains("description", skill.InvalidReason, StringComparison.Ordinal);
        Assert.Contains("not loaded", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_that_differs_from_the_directory_warns_naming_both()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        WriteSkill(Path.Combine(repo, ".agents", "skills", "greek"), "roman");

        SkillScanResult result = SkillScanner.Scan(Request(temp, worktree: repo, repoRoot: repo));

        SkillDescriptor skill = Assert.Single(result.Skills);
        Assert.False(skill.Valid);
        Assert.Contains("'roman'", skill.InvalidReason, StringComparison.Ordinal);
        Assert.Contains("'greek'", skill.InvalidReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Allowed_tools_is_accepted_and_ignored()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string directory = Path.Combine(repo, ".agents", "skills", "greek");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            "---\nname: greek\ndescription: D\nallowed-tools: [bash]\n---\nBody.\n"
        );

        SkillScanResult result = SkillScanner.Scan(Request(temp, worktree: repo, repoRoot: repo));

        Assert.True(Assert.Single(result.Skills).Valid);
    }

    [Fact]
    public void Disable_model_invocation_marks_the_skill_command_only()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string directory = Path.Combine(repo, ".agents", "skills", "greek");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            "---\nname: greek\ndescription: D\ndisable-model-invocation: true\n---\nBody.\n"
        );

        SkillScanResult result = SkillScanner.Scan(Request(temp, worktree: repo, repoRoot: repo));

        Assert.True(Assert.Single(result.Skills).CommandOnly);
    }

    [Fact]
    public void An_unreadable_skill_directory_is_skipped_without_a_skill()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string directory = Path.Combine(repo, ".agents", "skills", "not-a-skill");
        Directory.CreateDirectory(directory);

        SkillScanResult result = SkillScanner.Scan(Request(temp, worktree: repo, repoRoot: repo));

        Assert.Empty(result.Skills);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Discovery_stops_at_the_per_scope_cap()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        for (var index = 0; index < SkillScanner.MaxSkillsPerScope + 5; index++)
        {
            WriteSkill(
                Path.Combine(repo, ".agents", "skills", $"skill-{index:D3}"),
                $"skill-{index:D3}"
            );
        }

        SkillScanResult result = SkillScanner.Scan(Request(temp, worktree: repo, repoRoot: repo));

        Assert.Equal(SkillScanner.MaxSkillsPerScope, result.Skills.Count);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("more than 256", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void A_project_symlink_outside_the_repository_is_skipped()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string outside = temp.File("outside");
        WriteSkill(outside, "outside-skill");
        string skills = Path.Combine(repo, ".agents", "skills");
        Directory.CreateDirectory(skills);
        if (!TryCreateDirectoryLink(Path.Combine(skills, "outside-skill"), outside))
        {
            Assert.Skip("Symbolic links are not available in this environment.");
            return;
        }

        SkillScanResult result = SkillScanner.Scan(Request(temp, worktree: repo, repoRoot: repo));

        Assert.Empty(result.Skills);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("outside the repository", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void A_user_scoped_skill_is_discovered_and_its_directory_is_a_read_root()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string userSkill = Path.Combine(temp.File("home"), ".agents", "skills", "greek");
        WriteSkill(userSkill, "greek");

        SkillScanResult result = SkillScanner.Scan(Request(temp, worktree: repo, repoRoot: repo));

        SkillDescriptor skill = Assert.Single(result.Skills);
        Assert.Equal(SkillScope.User, skill.Scope);
        Assert.Contains(
            result.ReadRoots,
            root => root.Equals(Workspace.Canonicalize(userSkill), StringComparison.Ordinal)
        );
    }

    [Fact]
    public void An_extension_skill_is_discovered_and_tagged()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string extension = temp.File("extension");
        string skillDirectory = Path.Combine(extension, "skills", "greek");
        WriteSkill(skillDirectory, "greek");

        SkillScanResult result = SkillScanner.Scan(
            Request(
                temp,
                worktree: repo,
                repoRoot: repo,
                extensions: [new ExtensionSkillSource("hello", "main", extension, [skillDirectory])]
            )
        );

        SkillDescriptor skill = Assert.Single(result.Skills);
        Assert.Equal(SkillScope.Extension, skill.Scope);
        Assert.Equal("hello", skill.ExtensionId);
    }

    [Fact]
    public void An_extension_directory_outside_the_extensions_own_folder_is_skipped()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string extension = temp.File("extension");
        Directory.CreateDirectory(extension);
        string outside = Path.Combine(temp.File("elsewhere"), "greek");
        WriteSkill(outside, "greek");

        SkillScanResult result = SkillScanner.Scan(
            Request(
                temp,
                worktree: repo,
                repoRoot: repo,
                extensions: [new ExtensionSkillSource("hello", "main", extension, [outside])]
            )
        );

        Assert.Empty(result.Skills);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("own folder", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void A_user_skill_shadows_a_same_name_extension_skill()
    {
        using var temp = new TempDirectory();
        string repo = temp.File("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        WriteSkill(Path.Combine(temp.File("home"), ".agents", "skills", "greek"), "greek");
        string extension = temp.File("extension");
        string contributed = Path.Combine(extension, "skills", "greek");
        WriteSkill(contributed, "greek");

        SkillScanResult result = SkillScanner.Scan(
            Request(
                temp,
                worktree: repo,
                repoRoot: repo,
                extensions: [new ExtensionSkillSource("hello", "main", extension, [contributed])]
            )
        );

        SkillDescriptor skill = Assert.Single(result.Skills);
        Assert.Equal(SkillScope.User, skill.Scope);
        Assert.Equal("extension 'hello'", skill.Shadows);
    }

    /// <summary>A repository with a real linked worktree (<c>git worktree add</c>).</summary>
    private static (string Repository, string Worktree) LinkedWorktree(TempDirectory temp)
    {
        string repository = temp.File("repo");
        Directory.CreateDirectory(repository);
        GitTestRepo.Init(repository);
        GitTestRepo.Git(repository, "commit", "-q", "--allow-empty", "-m", "root");
        string worktree = temp.File("worktree");
        GitTestRepo.Git(repository, "worktree", "add", "-q", "-b", "linked", worktree);
        return (repository, worktree);
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or PlatformNotSupportedException
            )
        {
            return false;
        }
    }
}

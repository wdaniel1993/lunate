using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The host-side evaluator of manifest activation rules (tasks 5.1): always is true,
/// <c>workspace-root-has-file</c> matches direct children only — a literal pattern by exact ordinal
/// name, a single-segment <c>*</c> glob over ordinal names, never a subdirectory — an unreadable
/// root is false without throwing, and <c>workspace-is-git-repository</c> follows the host's
/// resolved git common dir.
/// </summary>
public sealed class ToolActivationEvaluatorTests
{
    // ── always ────────────────────────────────────────────────────────────

    [Fact]
    public void The_always_condition_is_true()
    {
        Assert.True(Evaluate(new ExtensionToolActivationRule("always", []), "anywhere"));
    }

    [Fact]
    public void The_always_condition_is_true_even_at_a_nonexistent_root()
    {
        using var temp = new TempDirectory();
        string missing = Path.Combine(temp.Root, "does-not-exist");

        Assert.True(Evaluate(new ExtensionToolActivationRule("always", []), missing));
    }

    [Fact]
    public void A_null_rule_is_always_active()
    {
        // null means "the manifest declared no rule" — the composition treats it as always.
        Assert.True(Evaluate(null, "anywhere"));
    }

    // ── workspace-root-has-file: literal patterns ─────────────────────────

    [Fact]
    public void A_literal_pattern_matches_only_that_exact_direct_child_name()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("README.md"), "readme");

        Assert.True(
            Evaluate(HasFile("README.md"), temp.Root),
            "the root's direct child 'README.md' must match the literal pattern"
        );
        Assert.False(
            Evaluate(HasFile("CONTRIBUTING.md"), temp.Root),
            "a literal pattern matches its own name only"
        );
    }

    [Fact]
    public void A_literal_pattern_is_ordinal_so_a_different_case_does_not_match()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("readme.md"), "readme");

        Assert.False(Evaluate(HasFile("README.MD"), temp.Root));
    }

    // ── workspace-root-has-file: single-segment globs ─────────────────────

    [Fact]
    public void An_extension_glob_matches_a_direct_child_whose_extension_is_the_pattern()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("App.sln"), "solution");

        Assert.True(Evaluate(HasFile("*.sln"), temp.Root));
    }

    [Fact]
    public void An_extension_glob_matches_every_name_whose_suffix_is_the_pattern()
    {
        // '*' swallows any characters before the suffix, any dot count: the flagship scan's rule.
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("App.x.sln"), "solution");

        Assert.True(Evaluate(HasFile("*.sln"), temp.Root));
    }

    [Fact]
    public void An_extension_glob_does_not_match_a_longer_extension()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("App.slnx"), "solution");

        Assert.False(Evaluate(HasFile("*.sln"), temp.Root));
    }

    [Fact]
    public void An_extension_glob_is_case_sensitive()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("App.SLN"), "solution");
        File.WriteAllText(temp.File("TODO.SLN"), "solution");

        Assert.False(Evaluate(HasFile("*.sln"), temp.Root));
        Assert.False(Evaluate(HasFile("todo.sln"), temp.Root));
    }

    [Fact]
    public void A_pattern_list_activates_when_one_pattern_matches()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("App.csproj"), "project");

        Assert.True(Evaluate(HasFile("*.sln", "*.csproj"), temp.Root));

        // The same list with no match at all stays inactive.
        Assert.False(Evaluate(HasFile("*.sln", "*.slnx"), temp.Root));
    }

    [Fact]
    public void A_wildcard_matches_any_characters_within_one_name_segment()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Build.Debug.props"), "props");

        Assert.True(Evaluate(HasFile("Build*.props"), temp.Root));
        Assert.False(Evaluate(HasFile("Build*.propsx"), temp.Root));
    }

    // ── workspace-root-has-file: direct children only, unreadable roots ───

    [Fact]
    public void Workspace_root_has_file_never_recurses()
    {
        using var temp = new TempDirectory();
        string nested = temp.Subdirectory("nested");
        File.WriteAllText(Path.Combine(nested, "App.sln"), "solution");

        Assert.False(
            Evaluate(HasFile("*.sln"), temp.Root),
            "a solution below the root is not a direct child"
        );

        // Depth one is the contract: the rule set at the nested root itself finds it.
        Assert.True(Evaluate(HasFile("*.sln"), nested));
    }

    [Fact]
    public void A_directory_named_like_a_pattern_is_not_a_file()
    {
        // Enumeration yields files only, so a directory carrying the pattern's name is skipped.
        using var temp = new TempDirectory();
        temp.Subdirectory("App.sln");

        Assert.False(Evaluate(HasFile("*.sln"), temp.Root));
    }

    [Fact]
    public void An_unreadable_root_is_false_without_throwing()
    {
        // A file at the root path: enumerating its "children" fails with an I/O error, and the
        // evaluator reports false instead of letting the failure become a condition.
        using var temp = new TempDirectory();
        string notADirectory = temp.File("plain-file");
        File.WriteAllText(notADirectory, "text");

        Assert.False(Evaluate(HasFile("*.sln"), notADirectory));
    }

    [Fact]
    public void A_root_without_read_permission_is_false_without_throwing()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows cannot chmod a directory into unreadability; the file-path test above
            // is the portable stand-in.
            return;
        }

        using var temp = new TempDirectory();
        string locked = temp.Subdirectory("locked");
        UnixFileMode original = File.GetUnixFileMode(locked);
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            Assert.False(Evaluate(HasFile("*.sln"), locked));
        }
        finally
        {
            // Restore first so TempDirectory cleanup can delete the tree, and so the test
            // succeeds when it runs again.
            File.SetUnixFileMode(locked, original);
        }
    }

    // ── workspace-is-git-repository ───────────────────────────────────────

    [Fact]
    public void A_worktree_root_with_a_git_common_dir_is_active()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Subdirectory(".git"));

        Assert.True(Evaluate(IsGitRepository(), temp.Root));
    }

    [Fact]
    public void A_root_outside_a_repository_is_not_active()
    {
        // TempDirectory sits directly under the temp path, outside any repository.
        using var temp = new TempDirectory();

        Assert.False(Evaluate(IsGitRepository(), temp.Root));
    }

    [Fact]
    public void A_real_git_repository_root_and_subdirectories_are_active()
    {
        using var temp = new TempDirectory();
        string repo = temp.Subdirectory("repo");
        GitTestRepo.Init(repo);

        Assert.True(Evaluate(IsGitRepository(), repo), "the repository root itself");
        string nested = temp.Subdirectory("repo/src");
        Assert.True(
            Evaluate(IsGitRepository(), nested),
            "a directory below the root through ancestor search"
        );
    }

    [Fact]
    public void A_linked_git_worktree_is_a_repository()
    {
        using var temp = new TempDirectory();
        string repo = temp.Subdirectory("repo");
        GitTestRepo.Init(repo);
        string worktree = temp.Subdirectory("worktree");
        GitTestRepo.Git(repo, "worktree", "add", worktree, "--detach");

        // A linked worktree's .git file points at the shared common dir.
        Assert.True(Evaluate(IsGitRepository(), worktree));
    }

    [Fact]
    public void The_resolved_git_common_dir_decides_the_git_condition_without_detection()
    {
        // The host-side composition already knows the common dir, and the three-argument overload
        // takes it as the verdict: null reads outside-a-repository even where the root says
        // otherwise, so no re-detection can contradict what the host resolved.
        using var temp = new TempDirectory();
        string gitDir = temp.Subdirectory(".git");
        var rule = new ExtensionToolActivationRule(
            ExtensionToolActivationRule.WorkspaceIsGitRepository,
            []
        );

        Assert.False(
            ToolActivationEvaluator.Evaluate(rule, temp.Root, gitCommonDir: null),
            "the resolved verdict is taken, not re-detected"
        );
        Assert.True(
            ToolActivationEvaluator.Evaluate(rule, temp.Root, gitCommonDir: gitDir),
            "the resolved common dir is the host's notion"
        );
    }

    private static bool Evaluate(ExtensionToolActivationRule? rule, string worktreeRoot) =>
        ToolActivationEvaluator.Evaluate(rule, worktreeRoot);

    private static ExtensionToolActivationRule HasFile(params string[] patterns) =>
        new(ExtensionToolActivationRule.WorkspaceRootHasFile, patterns);

    private static ExtensionToolActivationRule IsGitRepository() =>
        new(ExtensionToolActivationRule.WorkspaceIsGitRepository, []);
}

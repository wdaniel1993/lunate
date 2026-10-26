namespace Lunate.Agent.Tests;

/// <summary>
/// Repository identity is the nearest ancestor carrying a <c>.git</c> entry (ADR-0016): a session
/// launched from a subdirectory must resolve the same repository, and therefore the same trust
/// identity and the same <c>LUNATE_HOME</c> boundary, as one launched from the root.
/// </summary>
public sealed class GitIdentityTests
{
    [Fact]
    public void A_main_checkout_is_its_own_repository()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File(".git"));

        (string? repoRoot, string? commonDir) = GitIdentity.Detect(temp.Root);

        Assert.Equal(GitIdentity.Canonicalize(temp.Root), repoRoot);
        Assert.Equal(GitIdentity.Canonicalize(temp.File(".git")), commonDir);
    }

    [Fact]
    public void A_subdirectory_finds_the_repository_root()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File(".git"));
        string subdirectory = temp.File("src/App");
        Directory.CreateDirectory(subdirectory);

        (string? repoRoot, string? commonDir) = GitIdentity.Detect(subdirectory);

        Assert.Equal(GitIdentity.Canonicalize(temp.Root), repoRoot);
        Assert.Equal(GitIdentity.Canonicalize(temp.File(".git")), commonDir);
    }

    [Fact]
    public void A_subdirectory_of_a_linked_worktree_finds_the_main_repository()
    {
        using var temp = new TempDirectory();
        string main = temp.File("main");
        string worktree = temp.File("wt");
        string gitDir = Path.Combine(main, ".git", "worktrees", "wt");
        Directory.CreateDirectory(main);
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(gitDir, "commondir"), "../..\n");
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: ../main/.git/worktrees/wt\n");
        string subdirectory = Path.Combine(worktree, "src", "App");
        Directory.CreateDirectory(subdirectory);

        (string? repoRoot, string? commonDir) = GitIdentity.Detect(subdirectory);

        Assert.Equal(GitIdentity.Canonicalize(main), repoRoot);
        Assert.Equal(GitIdentity.Canonicalize(Path.Combine(main, ".git")), commonDir);
    }

    [Fact]
    public void The_nearest_repository_wins()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File(".git"));
        string nested = temp.File("vendor/dep");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(Path.Combine(nested, ".git"));
        string subdirectory = Path.Combine(nested, "src");
        Directory.CreateDirectory(subdirectory);

        (string? repoRoot, _) = GitIdentity.Detect(subdirectory);

        Assert.Equal(GitIdentity.Canonicalize(nested), repoRoot);
    }

    [Fact]
    public void A_directory_outside_a_repository_has_no_identity()
    {
        using var temp = new TempDirectory();
        string subdirectory = temp.File("plain/nested");
        Directory.CreateDirectory(subdirectory);

        (string? repoRoot, string? commonDir) = GitIdentity.Detect(subdirectory);

        Assert.Null(repoRoot);
        Assert.Null(commonDir);
    }

    [Fact]
    public void A_malformed_git_file_yields_nulls()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File(".git"), "not a gitdir line\n");

        (string? repoRoot, string? commonDir) = GitIdentity.Detect(temp.Root);

        Assert.Null(repoRoot);
        Assert.Null(commonDir);
    }
}

namespace Lunate.Tui.Tests;

public sealed class GitBranchReaderTests
{
    [Fact]
    public void Normal_repository_reads_the_checked_out_branch()
    {
        using var dir = new TempDir();
        WriteHead(dir.Path, ".git", "ref: refs/heads/feature/x\n");

        Assert.Equal("feature/x", GitBranchReader.Read(dir.Path));
    }

    [Fact]
    public void Worktree_git_file_follows_a_relative_gitdir()
    {
        using var dir = new TempDir();
        WriteHead(dir.Path, "main/.git/worktrees/wt", "ref: refs/heads/wt-branch\n");
        WriteAll(dir.Path, "wt/.git", "gitdir: ../main/.git/worktrees/wt\n");

        Assert.Equal("wt-branch", GitBranchReader.Read(Path.Combine(dir.Path, "wt")));
    }

    [Fact]
    public void Worktree_git_file_follows_an_absolute_gitdir()
    {
        using var dir = new TempDir();
        WriteHead(dir.Path, "main/.git/worktrees/wt", "ref: refs/heads/abs-branch\n");
        WriteAll(
            dir.Path,
            "wt/.git",
            $"gitdir: {Path.Combine(dir.Path, "main", ".git", "worktrees", "wt")}\n"
        );

        Assert.Equal("abs-branch", GitBranchReader.Read(Path.Combine(dir.Path, "wt")));
    }

    [Fact]
    public void Detached_head_shows_the_first_seven_sha_characters()
    {
        using var dir = new TempDir();
        WriteHead(dir.Path, ".git", "9c0f3a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f\n");

        Assert.Equal("9c0f3a1", GitBranchReader.Read(dir.Path));
    }

    [Fact]
    public void No_repository_yields_no_branch()
    {
        using var dir = new TempDir();

        Assert.Null(GitBranchReader.Read(dir.Path));
    }

    [Fact]
    public void Missing_directory_yields_no_branch()
    {
        using var dir = new TempDir();

        Assert.Null(GitBranchReader.Read(Path.Combine(dir.Path, "does-not-exist")));
    }

    [Fact]
    public void Unreadable_head_yields_no_branch()
    {
        using var dir = new TempDir();
        WriteHead(dir.Path, ".git", "ref: refs/heads/x\n");
        File.Delete(Path.Combine(dir.Path, ".git", "HEAD"));
        Directory.CreateDirectory(Path.Combine(dir.Path, ".git", "HEAD"));

        Assert.Null(GitBranchReader.Read(dir.Path));
    }

    [Fact]
    public void Empty_head_yields_no_branch()
    {
        using var dir = new TempDir();
        WriteHead(dir.Path, ".git", "");

        Assert.Null(GitBranchReader.Read(dir.Path));
    }

    [Fact]
    public void Git_file_without_a_gitdir_line_yields_no_branch()
    {
        using var dir = new TempDir();
        WriteAll(dir.Path, ".git", "not a gitdir line\n");

        Assert.Null(GitBranchReader.Read(dir.Path));
    }

    [Fact]
    public void Git_file_pointing_nowhere_yields_no_branch()
    {
        using var dir = new TempDir();
        WriteAll(dir.Path, ".git", "gitdir: ../missing\n");

        Assert.Null(GitBranchReader.Read(dir.Path));
    }

    private static void WriteHead(string root, string gitPath, string content) =>
        WriteAll(root, Path.Combine(gitPath, "HEAD"), content);

    private static void WriteAll(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lunate-git-").FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

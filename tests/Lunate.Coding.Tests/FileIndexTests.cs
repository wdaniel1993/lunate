namespace Lunate.Coding.Tests;

/// <summary>
/// Index behaviour through the in-memory seam: laziness, ordering, bounds, prefix queries and the
/// ignore chain (deeper wins, directory pruning), plus the real walk's `.git` and symlink rules.
/// </summary>
public sealed class FileIndexTests
{
    private static FileIndex Ready(FakeWorkspaceFiles files)
    {
        var index = new FileIndex(files);
        index.EnsureStarted();
        WaitReady(index);
        return index;
    }

    private static void WaitReady(FileIndex index)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!index.IsReady)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("the file index never became ready");
            }

            Thread.Sleep(1);
        }
    }

    private static bool ContainsGit(IReadOnlyList<string> matches) =>
        matches.Any(path =>
            string.Equals(path, ".git/", StringComparison.Ordinal)
            || path.StartsWith(".git/", StringComparison.Ordinal)
        );

    [Fact]
    public void Nothing_is_built_before_the_first_use()
    {
        var files = new FakeWorkspaceFiles().AddFile("a.txt");
        var index = new FileIndex(files);

        Assert.False(index.IsReady);
        Assert.False(index.IsTruncated);
        Assert.Equal(0, files.EnumerateCalls);
        Assert.Empty(index.Match("a", 10));

        index.EnsureStarted();
        WaitReady(index);

        Assert.True(index.IsReady);
        Assert.False(index.IsTruncated);
        Assert.Equal(1, files.EnumerateCalls);
    }

    [Fact]
    public void Entries_are_sorted_ordinal_and_directories_carry_a_trailing_slash()
    {
        var files = new FakeWorkspaceFiles()
            .AddFile("src/main.cs")
            .AddDirectory("src")
            .AddDirectory("src/core")
            .AddFile("README.md");
        FileIndex index = Ready(files);

        Assert.Equal(["README.md", "src/", "src/core/", "src/main.cs"], index.Match("", 100));
    }

    [Fact]
    public void Match_returns_the_ordinal_prefix_range_capped_at_max()
    {
        var files = new FakeWorkspaceFiles()
            .AddDirectory("src")
            .AddDirectory("src/cli")
            .AddDirectory("src/core")
            .AddFile("other.txt");
        FileIndex index = Ready(files);

        Assert.Equal(["src/cli/", "src/core/"], index.Match("src/c", 10));
        Assert.Equal(["src/"], index.Match("src/", 1));
        Assert.Empty(index.Match("nope", 10));
    }

    [Fact]
    public void Over_cap_entries_are_sorted_first_then_capped_and_flag_truncation()
    {
        var files = new FakeWorkspaceFiles();
        for (var number = 0; number <= FileIndex.EntryCap; number++)
        {
            files.AddFile($"f{number:D7}.txt");
        }

        FileIndex index = Ready(files);

        Assert.True(index.IsTruncated);
        Assert.Equal(FileIndex.EntryCap, index.Match("", 0).Count);
        Assert.Equal(["f0199999.txt"], index.Match("f0199999", 10));
        Assert.Empty(index.Match("f0200000", 10));
    }

    [Fact]
    public void Ignores_are_honoured_via_the_seam()
    {
        var files = new FakeWorkspaceFiles()
            .AddFile(".gitignore", "bin/\n*.log\n")
            .AddDirectory("bin")
            .AddFile("bin/app.dll")
            .AddDirectory("src")
            .AddFile("src/a.log")
            .AddFile("src/main.cs");
        FileIndex index = Ready(files);

        Assert.Empty(index.Match("bin", 10));
        Assert.Empty(index.Match("src/a.log", 10));
        Assert.Contains("src/main.cs", index.Match("src", 10));
    }

    [Fact]
    public void A_deeper_gitignore_wins_over_a_shallower_one()
    {
        var files = new FakeWorkspaceFiles()
            .AddFile(".gitignore", "*.txt\n")
            .AddDirectory("src")
            .AddFile("src/.gitignore", "!keep.txt\n")
            .AddFile("src/keep.txt")
            .AddFile("src/drop.txt")
            .AddFile("note.txt");
        FileIndex index = Ready(files);

        Assert.Contains("src/keep.txt", index.Match("src", 10));
        Assert.DoesNotContain("src/drop.txt", index.Match("src", 10));
        Assert.DoesNotContain("note.txt", index.Match("", 10));
    }

    [Fact]
    public void An_excluded_directory_prunes_its_subtree()
    {
        var files = new FakeWorkspaceFiles()
            .AddFile(".gitignore", "build/\n")
            .AddDirectory("build")
            .AddFile("build/.gitignore", "!keep.txt\n")
            .AddFile("build/keep.txt")
            .AddDirectory("src")
            .AddFile("src/main.cs");
        FileIndex index = Ready(files);

        Assert.Empty(index.Match("build", 10));
        Assert.Contains("src/", index.Match("src", 10));
    }

    [Fact]
    public void Git_never_appears_even_when_the_seam_offers_it()
    {
        var files = new FakeWorkspaceFiles()
            .AddDirectory(".git")
            .AddFile(".git/config")
            .AddFile(".gitignore")
            .AddDirectory("src")
            .AddFile("src/main.cs");
        FileIndex index = Ready(files);

        Assert.False(ContainsGit(index.Match(".git", 50)));
        Assert.Contains(".gitignore", index.Match(".git", 50));
        Assert.Contains("src/", index.Match("src", 10));
    }

    [Fact]
    public void The_real_walk_skips_git_and_honours_gitignore()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, "bin"));
        File.WriteAllText(Path.Combine(temp.Root, "bin", "app.dll"), string.Empty);
        Directory.CreateDirectory(Path.Combine(temp.Root, ".git"));
        File.WriteAllText(Path.Combine(temp.Root, ".git", "config"), string.Empty);
        Directory.CreateDirectory(Path.Combine(temp.Root, "src"));
        File.WriteAllText(Path.Combine(temp.Root, "src", "main.cs"), string.Empty);
        File.WriteAllText(Path.Combine(temp.Root, ".gitignore"), "bin/\n");

        var index = new FileIndex(new SystemWorkspaceFiles(temp.Root));
        index.EnsureStarted();
        WaitReady(index);

        Assert.Empty(index.Match("bin", 10));
        Assert.False(ContainsGit(index.Match(".git", 50)));
        Assert.Contains("src/", index.Match("src", 10));
        Assert.Contains("src/main.cs", index.Match("src", 10));
    }

    [Fact]
    public void The_real_walk_does_not_index_directory_symlinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, "real"));
        File.WriteAllText(Path.Combine(temp.Root, "real", "x.txt"), string.Empty);
        try
        {
            Directory.CreateSymbolicLink(
                Path.Combine(temp.Root, "link"),
                Path.Combine(temp.Root, "real")
            );
        }
        catch (IOException)
        {
            return;
        }

        var index = new FileIndex(new SystemWorkspaceFiles(temp.Root));
        index.EnsureStarted();
        WaitReady(index);

        Assert.Contains("real/x.txt", index.Match("real", 10));
        Assert.Empty(index.Match("link", 10));
    }
}

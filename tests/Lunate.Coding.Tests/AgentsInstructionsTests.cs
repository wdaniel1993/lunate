namespace Lunate.Coding.Tests;

public sealed class AgentsInstructionsTests
{
    [Fact]
    public void Missing_files_produce_no_instructions()
    {
        using var temp = new TempDirectory();

        string instructions = AgentsInstructions.Compose(temp.Root, temp.Root);

        Assert.Equal(string.Empty, instructions);
    }

    [Fact]
    public void Nested_files_concatenate_root_to_leaf_each_under_its_relative_path()
    {
        using var temp = new TempDirectory();
        string nested = Path.Combine(temp.Root, "src", "tools");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(temp.Root, "AGENTS.md"), "root rules");
        File.WriteAllText(Path.Combine(nested, "AGENTS.md"), "leaf rules");

        string instructions = AgentsInstructions.Compose(temp.Root, nested);

        Assert.Equal(
            "### AGENTS.md (AGENTS.md)\n\nroot rules\n\n### AGENTS.md (src/tools/AGENTS.md)\n\nleaf rules",
            instructions
        );
    }

    [Fact]
    public void Directories_without_a_file_are_skipped()
    {
        using var temp = new TempDirectory();
        string nested = Path.Combine(temp.Root, "a", "b");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "AGENTS.md"), "leaf only");

        string instructions = AgentsInstructions.Compose(temp.Root, nested);

        Assert.Equal("### AGENTS.md (a/b/AGENTS.md)\n\nleaf only", instructions);
    }

    [Fact]
    public void Working_directory_equal_to_the_root_reads_only_that_file()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Root, "AGENTS.md"), "only");

        string instructions = AgentsInstructions.Compose(temp.Root, temp.Root);

        Assert.Equal("### AGENTS.md (AGENTS.md)\n\nonly", instructions);
    }

    [Fact]
    public void A_root_that_is_not_an_ancestor_falls_back_to_the_working_directory()
    {
        using var temp = new TempDirectory();
        string root = Path.Combine(temp.Root, "main");
        string worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, "AGENTS.md"), "worktree rules");

        string instructions = AgentsInstructions.Compose(root, worktree);

        Assert.Equal("### AGENTS.md (AGENTS.md)\n\nworktree rules", instructions);
    }
}

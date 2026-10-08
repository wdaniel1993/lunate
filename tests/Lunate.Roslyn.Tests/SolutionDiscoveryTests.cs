using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class SolutionDiscoveryTests
{
    [Fact]
    public void A_missing_root_is_no_solution()
    {
        using var temp = new TempDirectory();

        var result = SolutionDiscovery.Discover(Path.Combine(temp.Root, "missing"));

        Assert.Null(result.SolutionPath);
        Assert.Contains("no solution", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_root_is_no_solution()
    {
        using var temp = new TempDirectory();

        var result = SolutionDiscovery.Discover(temp.Root);

        Assert.Null(result.SolutionPath);
        Assert.Contains(".sln", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_solution_in_the_root_is_found()
    {
        using var temp = new TempDirectory();
        var solution = temp.File("Only.slnx");
        System.IO.File.WriteAllText(solution, "<Solution />");

        var result = SolutionDiscovery.Discover(temp.Root);

        Assert.Equal(solution, result.SolutionPath);
    }

    [Fact]
    public void A_single_solution_one_level_down_is_found()
    {
        using var temp = new TempDirectory();
        var nested = temp.Subdirectory("nested");
        var solution = Path.Combine(nested, "Nested.sln");
        System.IO.File.WriteAllText(solution, "");

        var result = SolutionDiscovery.Discover(temp.Root);

        Assert.Equal(solution, result.SolutionPath);
    }

    [Fact]
    public void The_solution_named_after_the_root_wins_between_candidates()
    {
        using var temp = new TempDirectory();
        var worktree = temp.Subdirectory("demo");
        var expected = Path.Combine(worktree, "demo.slnx");
        System.IO.File.WriteAllText(expected, "<Solution />");
        System.IO.File.WriteAllText(Path.Combine(worktree, "other.sln"), "");

        var result = SolutionDiscovery.Discover(worktree);

        Assert.Equal(expected, result.SolutionPath);
    }

    [Fact]
    public void Ambiguous_candidates_are_no_solution_and_are_listed()
    {
        using var temp = new TempDirectory();
        System.IO.File.WriteAllText(temp.File("one.sln"), "");
        System.IO.File.WriteAllText(temp.File("two.slnx"), "<Solution />");

        var result = SolutionDiscovery.Discover(temp.Root);

        Assert.Null(result.SolutionPath);
        Assert.Contains("one.sln", result.Message, StringComparison.Ordinal);
        Assert.Contains("two.slnx", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_output_directories_are_not_scanned()
    {
        using var temp = new TempDirectory();
        var bin = temp.Subdirectory("bin");
        System.IO.File.WriteAllText(Path.Combine(bin, "stale.sln"), "");
        var expected = temp.File("Real.slnx");
        System.IO.File.WriteAllText(expected, "<Solution />");

        var result = SolutionDiscovery.Discover(temp.Root);

        Assert.Equal(expected, result.SolutionPath);
    }
}

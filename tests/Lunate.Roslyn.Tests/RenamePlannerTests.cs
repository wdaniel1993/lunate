using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class RenamePlannerTests
{
    [Fact]
    public void Line_changes_compare_normalized_eol_lines()
    {
        var changes = RenamePlanner.LineChanges("a\r\nb\r\nc", "a\nb2\nc");

        var change = Assert.Single(changes);
        Assert.Equal(2, change.Line);
        Assert.Equal("b", change.OldText);
        Assert.Equal("b2", change.NewText);
    }

    [Fact]
    public void Added_lines_are_reported()
    {
        var change = Assert.Single(RenamePlanner.LineChanges("a", "a\nb"));

        Assert.Equal(2, change.Line);
        Assert.Equal(string.Empty, change.OldText);
        Assert.Equal("b", change.NewText);
    }

    [Fact]
    public void Long_lines_are_bounded()
    {
        var change = Assert.Single(
            RenamePlanner.LineChanges(new string('a', 200), new string('b', 200))
        );

        Assert.Equal(120, change.OldText.Length);
        Assert.Equal(120, change.NewText.Length);
    }

    [Fact]
    public void The_cap_keeps_five_hundred_entries_and_counts_the_rest()
    {
        var oldText = string.Join('\n', Enumerable.Range(0, 501).Select(index => $"old{index}"));
        var newText = string.Join('\n', Enumerable.Range(0, 501).Select(index => $"new{index}"));

        var plan = RenamePlanner.Build([new RenameText("src/One.cs", oldText, newText)]);

        Assert.Equal(500, plan.Changes[0].Entries.Count);
        Assert.Equal(1, plan.TotalFileCount);
        Assert.Equal(501, plan.TotalChangeCount);
        Assert.True(plan.Truncated);
    }

    [Fact]
    public void File_order_is_preserved_in_the_plan()
    {
        var plan = RenamePlanner.Build([
            new RenameText("src/A.cs", "x", "y"),
            new RenameText("src/B.cs", "x", "y"),
        ]);

        Assert.Equal(["src/A.cs", "src/B.cs"], plan.Changes.Select(change => change.File));
        Assert.Equal(2, plan.TotalFileCount);
        Assert.Equal(2, plan.TotalChangeCount);
        Assert.False(plan.Truncated);
    }
}

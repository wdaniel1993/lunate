using Lunate.Tui;

namespace Lunate.Tui.Tests;

public sealed class AutocompleteStateTests
{
    private static CompletionItem Item(string text) =>
        new(text, null, CompletionKind.Command, null);

    [Fact]
    public void Move_clamps_at_both_ends()
    {
        var state = new AutocompleteState(
            [Item("a"), Item("b"), Item("c")],
            1,
            CompletionMode.Command,
            0
        );

        Assert.Equal(0, state.Move(-1).SelectedIndex);
        Assert.Equal(0, state.Move(-99).SelectedIndex);
        Assert.Equal(2, state.Move(1).Move(1).SelectedIndex);
        Assert.Equal(2, state.Move(99).SelectedIndex);
    }

    [Fact]
    public void Selected_is_the_clamped_item()
    {
        var state = new AutocompleteState([Item("a"), Item("b")], 7, CompletionMode.Command, 0);

        Assert.Equal("b", state.Selected?.Text);
        Assert.Null(new AutocompleteState([], 0, CompletionMode.Command, 0).Selected);
    }

    [Fact]
    public void A_file_item_carries_its_directory_part_as_the_muted_segment()
    {
        CompletionItem directory = CompletionItem.File("src/core/");
        Assert.Equal("src/core/", directory.Text);
        Assert.Equal("src/", directory.Muted);
        Assert.Equal(CompletionKind.File, directory.Kind);

        CompletionItem root = CompletionItem.File("README.md");
        Assert.Null(root.Muted);

        CompletionItem file = CompletionItem.File("src/main.cs");
        Assert.Equal("src/", file.Muted);
    }
}

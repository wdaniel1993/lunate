using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

public sealed class SelectListTests
{
    private static string FixturesDirectory =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "Lunate.Tui.Tests",
            "fixtures",
            "selectlist"
        );

    [Fact]
    public void Move_clamps_at_both_ends()
    {
        var model = new SelectListModel("Select model", ["a", "b", "c"], 1);

        Assert.Equal(0, model.Move(-1).Selected);
        Assert.Equal(0, model.Move(-99).Selected);
        Assert.Equal(2, model.Move(1).Move(1).Selected);
        Assert.Equal(2, model.Move(99).Selected);
    }

    [Fact]
    public void The_window_keeps_the_selection_visible()
    {
        var items = Enumerable.Range(1, 10).Select(index => $"item {index}").ToArray();

        var top = new SelectListModel("Pick", items, 0);
        Assert.Equal(
            ["item 1", "item 2", "item 3", "item 4", "item 5", "item 6", "item 7", "item 8"],
            SelectListRenderer.PlainItems(top)
        );

        var bottom = new SelectListModel("Pick", items, 9);
        Assert.Equal(
            ["item 3", "item 4", "item 5", "item 6", "item 7", "item 8", "item 9", "item 10"],
            SelectListRenderer.PlainItems(bottom)
        );
    }

    [Fact]
    public void Picker_block_matches_the_committed_golden()
    {
        var model = new SelectListModel(
            "Select model",
            [
                "* gpt-4o-mini (openai)",
                "gpt-4o (openai)",
                "claude-sonnet-5-5 (anthropic)",
                "claude-opus-5-5 (anthropic)",
                "local-llama (ollama)",
                "gpt-5 (openai)",
                "o4-mini (openai)",
                "gemini-3-pro (google)",
                "hidden-below (test)",
                "hidden-too (test)",
            ],
            1
        );

        string output = Render(model);

        GoldenFiles.AssertMatchesText(Path.Combine(FixturesDirectory, "model-picker.txt"), output);
    }

    [Fact]
    public void The_selected_item_is_emphasized()
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.EmitAnsiSequences = true;
        console.Write(new SelectListRenderer().Render(new SelectListModel("Pick", ["a", "b"], 1)));

        Assert.Contains("\u001b[1m> b", console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[1m  a", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Markup_in_labels_stays_literal()
    {
        var model = new SelectListModel("Pick", ["[red]danger[/]"], 0);

        Assert.Equal("Pick\n> [red]danger[/]\n", Render(model));
    }

    [Fact]
    public void An_empty_list_renders_only_the_title()
    {
        Assert.Equal("Pick\n", Render(new SelectListModel("Pick", [], 0)));
    }

    private static string Render(SelectListModel model)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Write(new SelectListRenderer().Render(model));
        return console.Output;
    }
}

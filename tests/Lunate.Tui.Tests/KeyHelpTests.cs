using Spectre.Console;

namespace Lunate.Tui.Tests;

public sealed class KeyHelpTests
{
    [Fact]
    public void Descriptions_match_the_guide_style()
    {
        Assert.Equal(
            "Ctrl+R",
            KeyPatternFormatter.Describe(new KeyPattern(KeyKind.Character, Ctrl: true, Text: "r"))
        );
        Assert.Equal(
            "Alt+Enter",
            KeyPatternFormatter.Describe(new KeyPattern(KeyKind.Enter, Alt: true))
        );
        Assert.Equal("Enter", KeyPatternFormatter.Describe(new KeyPattern(KeyKind.Enter)));
        Assert.Equal("Esc", KeyPatternFormatter.Describe(new KeyPattern(KeyKind.Escape)));
        Assert.Equal("Tab", KeyPatternFormatter.Describe(new KeyPattern(KeyKind.Tab)));
        Assert.Equal("Space", KeyPatternFormatter.Describe(new KeyPattern(KeyKind.Space)));
        Assert.Equal(
            "?",
            KeyPatternFormatter.Describe(new KeyPattern(KeyKind.Character, Text: "?"))
        );
        Assert.Equal(
            "Ctrl+J",
            KeyPatternFormatter.Describe(new KeyPattern(KeyKind.Character, Ctrl: true, Text: "j"))
        );
    }

    [Fact]
    public void Here_lists_only_the_bindings_that_apply()
    {
        KeyHelpModel model = KeyHelpModel.For(KeyContext.Idle);

        Assert.Contains(model.Rows, row => row.Action == "Send the message");
        Assert.DoesNotContain(model.Rows, row => row.Action == "Queue this message as a follow-up");
        Assert.DoesNotContain(model.Rows, row => row.Action == "Run the bash line");
        Assert.All(model.Rows, row => Assert.Equal("Editing", row.Group));
    }

    [Fact]
    public void All_lists_every_binding()
    {
        KeyHelpModel here = KeyHelpModel.For(KeyContext.Idle);
        KeyHelpModel all = here.WithScope(KeyHelpScope.All);

        Assert.True(all.Rows.Count > here.Rows.Count);
        Assert.Contains(all.Rows, row => row.Action == "Queue this message as a follow-up");
        Assert.Contains(all.Rows, row => row.Group == "Turn running");
        Assert.Equal(KeyHelpScope.All, all.Scope);
        Assert.Equal(here.Query, all.Query);
    }

    [Fact]
    public void Filter_matches_keys_and_actions_and_resets_the_selection()
    {
        KeyHelpModel model = KeyHelpModel.For(KeyContext.Idle).Move(3).Filter("stash");

        Assert.Single(model.Rows);
        Assert.Contains("Stash", model.Rows[0].Action, StringComparison.Ordinal);
        Assert.Equal(0, model.Selected);

        Assert.Empty(model.Filter("no such binding").Rows);
    }

    [Fact]
    public void Selection_is_clamped_at_both_ends()
    {
        KeyHelpModel model = KeyHelpModel.For(KeyContext.Idle);

        Assert.Equal(0, model.Move(-5).Selected);
        Assert.Equal(model.Rows.Count - 1, model.Move(500).Selected);
    }

    [Fact]
    public void Renderer_prints_the_scope_groups_and_the_empty_state()
    {
        string frame = Plain(KeyHelpModel.For(KeyContext.Idle));

        Assert.Contains("Keys — here", frame, StringComparison.Ordinal);
        Assert.Contains("Editing", frame, StringComparison.Ordinal);
        Assert.Contains("Send the message", frame, StringComparison.Ordinal);
        Assert.Contains("Esc close", frame, StringComparison.Ordinal);

        string empty = Plain(KeyHelpModel.For(KeyContext.Idle).Filter("zzzz"));
        Assert.Contains("no binding matches", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_catalog_row_has_a_description()
    {
        Assert.All(
            KeyBindingCatalog.All,
            binding => Assert.False(string.IsNullOrWhiteSpace(binding.Description))
        );
        Assert.All(
            KeyBindingCatalog.All,
            binding =>
                Assert.False(string.IsNullOrWhiteSpace(KeyPatternFormatter.Describe(binding.Key)))
        );
    }

    private static string Plain(KeyHelpModel model) => KeyHelpRenderer.ComposeMarkup(model);
}

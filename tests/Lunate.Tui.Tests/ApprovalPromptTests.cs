namespace Lunate.Tui.Tests;

public sealed class ApprovalPromptTests
{
    private static readonly ApprovalPromptModel Prompt = new("bash", "dotnet format");

    public static TheoryData<string, ApprovalChoice> Decisions =>
        new()
        {
            { "y", ApprovalChoice.Approve },
            { "n", ApprovalChoice.Deny },
            { "a", ApprovalChoice.Always },
        };

    [Theory]
    [MemberData(nameof(Decisions))]
    public void Bare_decision_keys_decide(string text, ApprovalChoice expected) =>
        Assert.Equal(expected, Prompt.Decide(Key(KeyKind.Character, text)));

    [Fact]
    public void Bare_enter_denies_as_the_safe_default() =>
        Assert.Equal(ApprovalChoice.Deny, Prompt.Decide(Key(KeyKind.Enter)));

    [Fact]
    public void Escape_denies_like_Claude_Code() =>
        Assert.Equal(ApprovalChoice.Deny, Prompt.Decide(Key(KeyKind.Escape)));

    [Fact]
    public void A_new_prompt_starts_on_no()
    {
        Assert.Equal(ApprovalPromptModel.DefaultSelected, Prompt.Selected);
        Assert.Equal(ApprovalChoice.Deny, Prompt.Selection);
        Assert.Equal(ApprovalChoice.Deny, Prompt.Activate());
    }

    [Fact]
    public void Space_on_an_untouched_prompt_denies_like_enter() =>
        Assert.Equal(Prompt.Decide(Key(KeyKind.Enter)), Prompt.Decide(Key(KeyKind.Space)));

    [Fact]
    public void Up_from_no_lands_on_yes() => Assert.Equal(0, Prompt.Move(-1).Selected);

    [Fact]
    public void Down_from_yes_lands_on_no() => Assert.Equal(1, Prompt.Move(-1).Move(1).Selected);

    [Fact]
    public void Down_from_always_wraps_to_yes() => Assert.Equal(0, Prompt.Move(1).Move(1).Selected);

    [Fact]
    public void Up_from_yes_wraps_to_always() => Assert.Equal(2, Prompt.Move(-1).Move(-1).Selected);

    [Fact]
    public void Space_activates_the_moved_selection() =>
        Assert.Equal(ApprovalChoice.Approve, Prompt.Move(-1).Decide(Key(KeyKind.Space)));

    [Fact]
    public void A_letter_decides_over_the_selection()
    {
        ApprovalPromptModel onAlways = Prompt.Move(1);

        Assert.Equal(2, onAlways.Selected);
        Assert.Equal(ApprovalChoice.Deny, onAlways.Decide(Key(KeyKind.Character, "n")));
    }

    [Fact]
    public void Only_up_and_down_move_the_selection()
    {
        Assert.True(Prompt.Moves(Key(KeyKind.Up)));
        Assert.True(Prompt.Moves(Key(KeyKind.Down)));

        foreach (
            KeyKind kind in new[] { KeyKind.Left, KeyKind.Right, KeyKind.Tab, KeyKind.PageDown }
        )
        {
            KeyEvent key = Key(kind);
            Assert.False(Prompt.Moves(key));
            Assert.Null(Prompt.Decide(key));
        }

        Assert.False(Prompt.Moves(Key(KeyKind.Character, "k")));
        Assert.Null(Prompt.Decide(Key(KeyKind.Character, "k")));
    }

    [Fact]
    public void A_modified_arrow_never_moves_the_selection()
    {
        Assert.False(Prompt.Moves(Key(KeyKind.Up, ctrl: true)));
        Assert.False(Prompt.Moves(Key(KeyKind.Down, alt: true)));
        Assert.False(Prompt.Moves(Key(KeyKind.Up, shift: true)));
    }

    [Fact]
    public void Question_mark_shows_the_legend_and_decides_nothing()
    {
        KeyEvent key = Key(KeyKind.Character, "?");

        Assert.True(Prompt.ShowsLegend(key));
        Assert.Null(Prompt.Decide(key));

        ApprovalPromptModel shown = Prompt.Move(1).ShowKeyLegend();
        Assert.True(shown.ShowLegend);
        Assert.Equal(2, shown.Selected);
        Assert.Null(shown.Decide(key));
    }

    [Theory]
    [InlineData(KeyKind.Character, "y", true, false, false)]
    [InlineData(KeyKind.Character, "n", false, true, false)]
    [InlineData(KeyKind.Character, "a", false, false, true)]
    [InlineData(KeyKind.Enter, null, true, false, false)]
    [InlineData(KeyKind.Enter, null, false, true, false)]
    [InlineData(KeyKind.Enter, null, false, false, true)]
    [InlineData(KeyKind.Space, null, true, false, false)]
    public void Modifiers_never_decide(
        KeyKind kind,
        string? text,
        bool ctrl,
        bool shift,
        bool alt
    ) => Assert.Null(Prompt.Decide(new KeyEvent(kind, text, ctrl, shift, alt)));

    [Theory]
    [InlineData("Y")]
    [InlineData("N")]
    [InlineData("A")]
    [InlineData("x")]
    [InlineData("")]
    public void Non_decision_characters_do_not_decide(string text) =>
        Assert.Null(Prompt.Decide(Key(KeyKind.Character, text)));

    [Theory]
    [InlineData(KeyKind.Tab)]
    [InlineData(KeyKind.Paste)]
    public void Other_keys_do_not_decide(KeyKind kind) => Assert.Null(Prompt.Decide(Key(kind)));

    private static KeyEvent Key(
        KeyKind kind,
        string? text = null,
        bool ctrl = false,
        bool shift = false,
        bool alt = false
    ) => new(kind, text, ctrl, shift, alt);
}

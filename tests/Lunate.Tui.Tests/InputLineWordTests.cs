using Lunate.Tui;

namespace Lunate.Tui.Tests;

public sealed class InputLineWordTests
{
    [Theory]
    [InlineData("git commit -m x", 15, 14)]
    [InlineData("git commit -m x", 10, 4)]
    [InlineData("git commit -m x", 4, 0)]
    [InlineData("git commit -m x", 0, 0)]
    public void MoveWordLeft_lands_on_the_previous_word_start(string text, int cursor, int expected)
    {
        InputLineState state = new(text, cursor);

        Assert.Equal(expected, InputLine.MoveWordLeft(state).CursorPosition);
    }

    [Theory]
    [InlineData("git commit -m x", 0, 3)]
    [InlineData("git commit -m x", 3, 10)]
    [InlineData("git commit -m x", 15, 15)]
    public void MoveWordRight_lands_at_the_end_of_the_next_word(
        string text,
        int cursor,
        int expected
    )
    {
        InputLineState state = new(text, cursor);

        Assert.Equal(expected, InputLine.MoveWordRight(state).CursorPosition);
    }

    [Fact]
    public void MoveWordRight_from_inside_a_word_stops_at_its_end()
    {
        InputLineState state = new("alpha   beta", 2);

        Assert.Equal(5, InputLine.MoveWordRight(state).CursorPosition);
    }

    [Fact]
    public void MoveWordLeft_crosses_separators_before_the_word()
    {
        InputLineState state = new("alpha   beta", 11);

        Assert.Equal(8, InputLine.MoveWordLeft(state).CursorPosition);
    }

    [Fact]
    public void Word_motion_never_splits_a_surrogate_pair()
    {
        InputLineState state = new("a 😀 b", 5);

        int position = InputLine.MoveWordLeft(state).CursorPosition;

        Assert.True(position == 0 || !char.IsLowSurrogate(state.Text[position]));
    }

    [Fact]
    public void KillWordBack_kills_the_word_before_the_cursor()
    {
        InputLineState state = new("alpha beta", 10);

        InputLine.KillResult kill = InputLine.KillWordBack(state);

        Assert.Equal("beta", kill.Killed);
        Assert.Equal("alpha ", kill.State.Text);
        Assert.Equal(6, kill.State.CursorPosition);
    }

    [Fact]
    public void KillWordBack_keeps_the_earlier_words()
    {
        InputLineState state = new("alpha beta gamma", 10);

        InputLine.KillResult kill = InputLine.KillWordBack(state);

        Assert.Equal("beta", kill.Killed);
        Assert.Equal("alpha  gamma", kill.State.Text);
        Assert.Equal(6, kill.State.CursorPosition);
    }

    [Fact]
    public void KillToEnd_and_KillToStart_are_inverse_halves()
    {
        InputLineState state = new("git commit", 3);

        InputLine.KillResult tail = InputLine.KillToEnd(state);
        Assert.Equal("git", tail.State.Text);
        Assert.Equal(" commit", tail.Killed);

        InputLine.KillResult head = InputLine.KillToStart(state);
        Assert.Equal(" commit", head.State.Text);
        Assert.Equal("git", head.Killed);
        Assert.Equal(0, head.State.CursorPosition);
    }

    [Fact]
    public void KillWordForward_takes_through_the_next_word()
    {
        InputLineState state = new("alpha beta", 0);

        InputLine.KillResult kill = InputLine.KillWordForward(state);

        Assert.Equal("alpha", kill.Killed);
        Assert.Equal(" beta", kill.State.Text);
    }

    [Fact]
    public void KillWordForward_at_the_end_kills_nothing()
    {
        InputLineState state = new("alpha", 5);

        Assert.Equal("", InputLine.KillWordForward(state).Killed);
    }

    [Fact]
    public void Word_ops_respect_cjk_letters()
    {
        InputLineState state = new("你好 世界", 5);

        Assert.Equal(3, InputLine.MoveWordLeft(state).CursorPosition);

        InputLine.KillResult kill = InputLine.KillWordBack(state);
        Assert.Equal("世界", kill.Killed);
        Assert.Equal("你好 ", kill.State.Text);
    }
}

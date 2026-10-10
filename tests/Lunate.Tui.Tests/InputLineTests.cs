namespace Lunate.Tui.Tests;

public sealed class InputLineTests
{
    [Fact]
    public void Insert_adds_text_at_the_cursor()
    {
        var state = new InputLineState("ac", 1);

        var result = InputLine.Insert(state, "b");

        Assert.Equal(new InputLineState("abc", 2), result);
    }

    [Fact]
    public void Paste_inserts_literally_and_moves_the_cursor_after_it()
    {
        var result = InputLine.Apply(
            new InputLineState("", 0),
            new KeyEvent(KeyKind.Paste, "line one\nline two", false, false, false)
        );

        Assert.Equal(new InputLineState("line one\nline two", 17), result);
    }

    [Fact]
    public void Backspace_at_the_start_is_a_noop()
    {
        var state = new InputLineState("abc", 0);

        Assert.Equal(state, InputLine.Backspace(state));
    }

    [Fact]
    public void Backspace_removes_the_character_before_the_cursor()
    {
        var result = InputLine.Backspace(new InputLineState("abc", 2));

        Assert.Equal(new InputLineState("ac", 1), result);
    }

    [Fact]
    public void Backspace_removes_a_whole_surrogate_pair()
    {
        var result = InputLine.Backspace(new InputLineState("a🙂", 3));

        Assert.Equal(new InputLineState("a", 1), result);
    }

    [Fact]
    public void Delete_at_the_end_is_a_noop()
    {
        var state = new InputLineState("abc", 3);

        Assert.Equal(state, InputLine.Delete(state));
    }

    [Fact]
    public void Delete_removes_the_character_at_the_cursor()
    {
        var result = InputLine.Delete(new InputLineState("abc", 1));

        Assert.Equal(new InputLineState("ac", 1), result);
    }

    [Fact]
    public void Delete_removes_a_whole_surrogate_pair()
    {
        var result = InputLine.Delete(new InputLineState("a🙂", 1));

        Assert.Equal(new InputLineState("a", 1), result);
    }

    [Fact]
    public void Move_left_stops_at_the_start()
    {
        var state = new InputLineState("abc", 0);

        Assert.Equal(state, InputLine.MoveLeft(state));
    }

    [Fact]
    public void Move_left_skips_a_whole_surrogate_pair()
    {
        var result = InputLine.MoveLeft(new InputLineState("a🙂", 3));

        Assert.Equal(new InputLineState("a🙂", 1), result);
    }

    [Fact]
    public void Move_right_stops_at_the_end()
    {
        var state = new InputLineState("abc", 3);

        Assert.Equal(state, InputLine.MoveRight(state));
    }

    [Fact]
    public void Move_right_skips_a_whole_surrogate_pair()
    {
        var result = InputLine.MoveRight(new InputLineState("a🙂", 1));

        Assert.Equal(new InputLineState("a🙂", 3), result);
    }

    [Fact]
    public void Home_and_end_move_to_both_ends()
    {
        var state = new InputLineState("abc", 1);

        Assert.Equal(new InputLineState("abc", 0), InputLine.MoveHome(state));
        Assert.Equal(new InputLineState("abc", 3), InputLine.MoveEnd(state));
    }

    [Theory]
    [InlineData(KeyKind.Enter, true, null, false, "\n")]
    [InlineData(KeyKind.Character, false, "j", true, "\n")]
    public void Newline_inserts_through_ctrl_j_and_alt_enter(
        KeyKind kind,
        bool alt,
        string? text,
        bool ctrl,
        string expected
    )
    {
        var result = InputLine.Apply(
            new InputLineState("ab", 1),
            new KeyEvent(kind, text, ctrl, false, alt)
        );

        Assert.Equal(new InputLineState("a" + expected + "b", 2), result);
    }

    [Fact]
    public void Shift_enter_inserts_a_newline_where_terminals_report_it()
    {
        var result = InputLine.Apply(
            new InputLineState("ab", 1),
            new KeyEvent(KeyKind.Enter, null, false, true, false)
        );

        Assert.Equal(new InputLineState("a\nb", 2), result);
    }

    [Fact]
    public void Ctrl_character_other_than_j_is_ignored()
    {
        var state = new InputLineState("ab", 1);

        Assert.Equal(
            state,
            InputLine.Apply(state, new KeyEvent(KeyKind.Character, "c", true, false, false))
        );
    }

    [Fact]
    public void Alt_character_is_ignored()
    {
        var state = new InputLineState("ab", 1);

        Assert.Equal(
            state,
            InputLine.Apply(state, new KeyEvent(KeyKind.Character, "x", false, false, true))
        );
    }

    [Fact]
    public void Space_inserts_a_space()
    {
        var result = InputLine.Apply(
            new InputLineState("ab", 1),
            new KeyEvent(KeyKind.Space, null, false, false, false)
        );

        Assert.Equal(new InputLineState("a b", 2), result);
    }

    [Fact]
    public void Unknown_keys_are_ignored()
    {
        var state = new InputLineState("ab", 1);

        Assert.Equal(
            state,
            InputLine.Apply(state, new KeyEvent(KeyKind.Unknown, null, false, false, false))
        );
    }

    [Fact]
    public void Empty_layout_shows_the_prompt_and_places_the_cursor_after_it()
    {
        var layout = InputLine.Layout(new InputLineState("", 0), width: 10, maxLines: 8);

        Assert.Equal(["> "], layout.Lines);
        Assert.Equal(0, layout.CursorRow);
        Assert.Equal(2, layout.CursorColumn);
    }

    [Fact]
    public void Cursor_column_uses_cell_widths()
    {
        var layout = InputLine.Layout(new InputLineState("中a", 2), width: 10, maxLines: 8);

        Assert.Equal(["> 中a"], layout.Lines);
        Assert.Equal(5, layout.CursorColumn);
    }

    [Fact]
    public void The_buffer_wraps_at_the_terminal_width()
    {
        var layout = InputLine.Layout(new InputLineState("abcdef", 6), width: 6, maxLines: 8);

        Assert.Equal(["> abcd", "  ef"], layout.Lines);
        Assert.Equal(1, layout.CursorRow);
        Assert.Equal(4, layout.CursorColumn);
    }

    [Fact]
    public void Wide_characters_wrap_by_cell_width()
    {
        var layout = InputLine.Layout(new InputLineState("中中", 2), width: 4, maxLines: 8);

        Assert.Equal(["> 中", "  中"], layout.Lines);
        Assert.Equal(1, layout.CursorRow);
        Assert.Equal(4, layout.CursorColumn);
    }

    [Fact]
    public void Embedded_newlines_split_display_lines()
    {
        var layout = InputLine.Layout(new InputLineState("a\nb", 3), width: 10, maxLines: 8);

        Assert.Equal(["> a", "  b"], layout.Lines);
        Assert.Equal(1, layout.CursorRow);
        Assert.Equal(3, layout.CursorColumn);
    }

    [Fact]
    public void Display_lines_are_bounded_around_the_cursor()
    {
        var layout = InputLine.Layout(new InputLineState("a\nb\nc\nd", 7), width: 10, maxLines: 2);

        Assert.Equal(["  c", "  d"], layout.Lines);
        Assert.Equal(1, layout.CursorRow);
        Assert.Equal(3, layout.CursorColumn);
    }
}

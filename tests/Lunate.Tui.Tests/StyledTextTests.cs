namespace Lunate.Tui.Tests;

/// <summary>
/// The styled-line word wrapper the transcript viewer composes with: fitting lines stay untouched,
/// overlong lines break at their last space or mid-word, styles carry across breaks, and embedded
/// newlines split first.
/// </summary>
public sealed class StyledTextTests
{
    [Fact]
    public void A_fitting_line_keeps_its_text_and_styles() =>
        Assert.Equal(
            ["[bold]one[/] two"],
            Markup(
                StyledText.Wrap(
                    [Line(("one", SpanStyle.Bold), (" two", SpanStyle.Plain))],
                    width: 20
                )
            )
        );

    [Fact]
    public void Adjacent_spans_with_the_same_style_coalesce() =>
        Assert.Equal(
            ["[bold]one two[/]"],
            Markup(
                StyledText.Wrap(
                    [Line(("one ", SpanStyle.Bold), ("two", SpanStyle.Bold))],
                    width: 20
                )
            )
        );

    [Fact]
    public void An_overlong_line_breaks_at_its_last_space() =>
        Assert.Equal(
            ["one two", "three"],
            Markup(StyledText.Wrap([Line(("one two three", SpanStyle.Plain))], width: 7))
        );

    [Fact]
    public void Leading_indentation_survives_a_wrap() =>
        Assert.Equal(
            ["  one two", "three"],
            Markup(StyledText.Wrap([Line(("  one two three", SpanStyle.Plain))], width: 9))
        );

    [Fact]
    public void A_word_longer_than_the_width_hard_breaks_at_the_boundary() =>
        Assert.Equal(
            ["abc", "def", "gh"],
            Markup(StyledText.Wrap([Line(("abcdefgh", SpanStyle.Plain))], width: 3))
        );

    [Fact]
    public void A_span_style_carries_across_a_break() =>
        Assert.Equal(
            ["aa", "[bold]bbb[/]", "[bold]b[/]"],
            Markup(
                StyledText.Wrap(
                    [Line(("aa ", SpanStyle.Plain), ("bbbb", SpanStyle.Bold))],
                    width: 3
                )
            )
        );

    [Fact]
    public void An_embedded_newline_splits_into_separate_lines() =>
        Assert.Equal(
            ["one", "two"],
            Markup(StyledText.Wrap([Line(("one\ntwo", SpanStyle.Plain))], width: 20))
        );

    [Fact]
    public void A_blank_hard_line_stays_blank() =>
        Assert.Equal(
            ["one", "", "two"],
            Markup(StyledText.Wrap([Line(("one\n\ntwo", SpanStyle.Plain))], width: 20))
        );

    [Fact]
    public void Wide_runes_count_two_cells_and_break_at_the_boundary() =>
        Assert.Equal(
            ["日本", "語"],
            Markup(StyledText.Wrap([Line(("\u65e5\u672c\u8a9e", SpanStyle.Plain))], width: 4))
        );

    private static StyledLine Line(params (string Text, SpanStyle Style)[] spans)
    {
        var line = new StyledLine();
        foreach (var (text, style) in spans)
        {
            line.Add(text, style);
        }

        return line;
    }

    private static string[] Markup(IReadOnlyList<StyledLine> lines) =>
        lines.Select(line => line.ToMarkup()).ToArray();
}

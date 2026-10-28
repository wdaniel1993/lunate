namespace Lunate.Tui.Tests;

public sealed class CellTextTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("abc", 3)]
    [InlineData("ä", 1)]
    [InlineData("e\u0301", 1)]
    [InlineData("中", 2)]
    [InlineData("日本語", 6)]
    [InlineData("中a", 3)]
    [InlineData("🙂", 2)]
    [InlineData("\u0007", 0)]
    [InlineData("\uFE0F", 0)]
    [InlineData("→", 1)]
    public void Width_table(string text, int expected) =>
        Assert.Equal(expected, CellText.Width(text));

    [Theory]
    [InlineData("abcdef", 3, "abc")]
    [InlineData("中文a", 3, "中")]
    [InlineData("abc", 0, "")]
    [InlineData("a\u0301b", 1, "a\u0301")]
    public void Clip_never_exceeds_the_cell_width(string text, int width, string expected) =>
        Assert.Equal(expected, CellText.Clip(text, width));

    [Fact]
    public void Middle_clip_keeps_both_ends_with_one_ellipsis() =>
        Assert.Equal("abcd…ghij", CellText.MiddleClip("abcdefghij", 9));

    [Fact]
    public void Middle_clip_gives_the_extra_cell_to_the_head() =>
        Assert.Equal("abcd…hij", CellText.MiddleClip("abcdefghij", 8));

    [Fact]
    public void Middle_clip_uses_the_ascii_ellipsis_when_requested() =>
        Assert.Equal("abc...ij", CellText.MiddleClip("abcdefghij", 8, unicode: false));

    [Fact]
    public void Middle_clip_leaves_text_that_fits_unchanged() =>
        Assert.Equal("abc", CellText.MiddleClip("abc", 5));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Middle_clip_returns_empty_when_the_width_is_not_positive(int width) =>
        Assert.Equal(string.Empty, CellText.MiddleClip("abcdef", width));

    [Fact]
    public void Middle_clip_keeps_whole_wide_runes_within_the_budget() =>
        Assert.Equal("日…語", CellText.MiddleClip("日本語", 5));

    [Theory]
    [InlineData("abcdefghij", 1)]
    [InlineData("abcdefghij", 2)]
    [InlineData("abcdefghij", 7)]
    [InlineData("abcdefghij", 8)]
    [InlineData("日本語abc", 2)]
    [InlineData("日本語abc", 4)]
    [InlineData("日本語abc", 5)]
    [InlineData("a🙂b🙂c", 4)]
    public void Middle_clip_never_exceeds_the_cell_budget(string text, int width) =>
        Assert.True(
            CellText.Width(CellText.MiddleClip(text, width)) <= width,
            $"{CellText.MiddleClip(text, width)} exceeds {width}"
        );

    [Theory]
    [InlineData("a\r\nb\tc  d", "a b c d")]
    [InlineData("a\u0001b", "ab")]
    [InlineData("a\u007fb", "ab")]
    [InlineData("  \r\n\t ", "")]
    public void SingleLine_collapses_whitespace_and_drops_control_characters(
        string text,
        string expected
    ) => Assert.Equal(expected, CellText.SingleLine(text));
}

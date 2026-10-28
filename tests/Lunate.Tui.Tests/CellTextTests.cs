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

    [Fact]
    public void Wrap_returns_text_that_fits_unchanged() =>
        Assert.Equal(["abc"], CellText.Wrap("abc", 5));

    [Fact]
    public void Wrap_keeps_every_character_and_respects_the_cell_width()
    {
        const string Path = "/srv/trusted/../untrusted/ext";

        IReadOnlyList<string> lines = CellText.Wrap(Path, 10);

        Assert.Equal(Path, string.Concat(lines));
        Assert.All(
            lines,
            line => Assert.True(CellText.Width(line) <= 10, $"'{line}' exceeds 10 cells")
        );
    }

    [Fact]
    public void Wrap_breaks_at_a_space_before_a_path_separator()
    {
        IReadOnlyList<string> lines = CellText.Wrap("/one/two and three", 12);

        Assert.Equal(["/one/two ", "and three"], lines);
    }

    [Fact]
    public void Wrap_breaks_a_segment_at_its_path_separators()
    {
        IReadOnlyList<string> lines = CellText.Wrap("/aaaa/bbbb/cccc", 6);

        Assert.Equal(["/aaaa/", "bbbb/", "cccc"], lines);
        Assert.Equal("/aaaa/bbbb/cccc", string.Concat(lines));
    }

    [Fact]
    public void Wrap_keeps_wide_runes_whole()
    {
        IReadOnlyList<string> lines = CellText.Wrap("日本語abc", 5);

        Assert.Equal(["日本", "語abc"], lines);
        Assert.All(lines, line => Assert.True(CellText.Width(line) <= 5));
    }
}

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
}

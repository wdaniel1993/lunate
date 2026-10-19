namespace Lunate.Tui.Tests;

public sealed class OutputExcerptTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Blank_output_has_no_lines(string? output) =>
        Assert.Empty(OutputExcerpt.Build(output));

    [Fact]
    public void Seventeen_lines_render_whole()
    {
        string output = string.Join('\n', Numbers(17));

        var lines = OutputExcerpt.Build(output);

        Assert.Equal(17, lines.Count);
        Assert.DoesNotContain(lines, line => line.IsMarker);
        Assert.Equal("line 1", lines[0].Text);
        Assert.Equal("line 17", lines[^1].Text);
    }

    [Fact]
    public void Eighteen_lines_elide_the_middle()
    {
        string output = string.Join('\n', Numbers(18));

        var lines = OutputExcerpt.Build(output);

        Assert.Equal(17, lines.Count);
        Assert.Equal("line 1", lines[0].Text);
        Assert.Equal(new OutputExcerptLine("\u2026 +2 lines \u00b7 ctrl+o", true), lines[8]);
        Assert.Equal("line 18", lines[^1].Text);
    }

    [Fact]
    public void Long_output_reports_the_exact_hidden_count()
    {
        string output = string.Join('\n', Numbers(100));

        var lines = OutputExcerpt.Build(output);

        Assert.Equal(17, lines.Count);
        Assert.Equal("\u2026 +84 lines \u00b7 ctrl+o", lines[8].Text);
        Assert.Equal("line 93", lines[9].Text);
    }

    [Fact]
    public void The_ascii_marker_uses_ascii_punctuation()
    {
        string output = string.Join('\n', Numbers(18));

        var lines = OutputExcerpt.Build(output, ExcerptMode.HeadAndTail, unicode: false);

        Assert.Equal(new OutputExcerptLine("... +2 lines - ctrl+o", true), lines[8]);
    }

    [Fact]
    public void Tail_mode_keeps_the_last_five_lines_then_the_marker()
    {
        string output = string.Join('\n', Numbers(8));

        var lines = OutputExcerpt.Build(output, ExcerptMode.TailOnly);

        Assert.Equal(6, lines.Count);
        Assert.Equal("line 4", lines[0].Text);
        Assert.Equal("line 8", lines[4].Text);
        Assert.Equal(new OutputExcerptLine("\u2026 +3 lines \u00b7 ctrl+o", true), lines[5]);
    }

    [Fact]
    public void Tail_mode_shows_output_within_the_budget_whole()
    {
        string output = string.Join('\n', Numbers(5));

        var lines = OutputExcerpt.Build(output, ExcerptMode.TailOnly);

        Assert.Equal(5, lines.Count);
        Assert.Equal("line 1", lines[0].Text);
        Assert.DoesNotContain(lines, line => line.IsMarker);
    }

    [Fact]
    public void A_single_trailing_newline_is_trimmed()
    {
        var lines = OutputExcerpt.Build("one\ntwo\n");

        Assert.Equal(2, lines.Count);
        Assert.Equal(["one", "two"], lines.Select(line => line.Text));
    }

    [Fact]
    public void Carriage_returns_are_normalized()
    {
        var lines = OutputExcerpt.Build("one\r\ntwo\r\n");

        Assert.Equal(["one", "two"], lines.Select(line => line.Text));
    }

    [Fact]
    public void Blank_lines_inside_the_output_are_preserved_and_counted()
    {
        string output = string.Join('\n', Numbers(9)) + "\n\n" + string.Join('\n', Numbers(9));

        var lines = OutputExcerpt.Build(output);

        Assert.Equal(17, lines.Count);
        Assert.Equal("\u2026 +3 lines \u00b7 ctrl+o", lines[8].Text);
    }

    private static IEnumerable<string> Numbers(int count) =>
        Enumerable.Range(1, count).Select(number => $"line {number}");
}

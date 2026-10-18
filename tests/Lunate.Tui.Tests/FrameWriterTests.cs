namespace Lunate.Tui.Tests;

public sealed class FrameWriterTests
{
    [Fact]
    public void First_paint_has_no_cursor_up_and_clears_each_line()
    {
        using var console = new FakeConsoleIO();
        var writer = new FrameWriter(console);

        writer.Paint(new RenderedFrame(["> a"], 0, 3));

        Assert.Equal("\r\u001b[2K> a\n\u001b[1A\r\u001b[3C", console.Writes[0]);
    }

    [Fact]
    public void Repaints_start_at_the_previous_frame_top()
    {
        // The cursor rests on the frame's cursor row (the input line), not below the frame:
        // the next paint must move up by exactly that row. Moving up by the frame height
        // would overshoot, drift the area upward and leave unerased ghost rows behind.
        using var console = new FakeConsoleIO();
        var writer = new FrameWriter(console);

        writer.Paint(new RenderedFrame(["status", "> a"], 1, 3));
        writer.Paint(new RenderedFrame(["status", "> ab"], 1, 4));

        Assert.EndsWith("\u001b[1A\r\u001b[3C", console.Writes[0]);

        string second = console.Writes[1];
        Assert.StartsWith("\u001b[1A", second);
        Assert.Contains("\r\u001b[2Kstatus\n", second);
        Assert.Contains("\r\u001b[2K> ab\n", second);
        Assert.EndsWith("\u001b[1A\r\u001b[4C", second);
    }

    [Fact]
    public void Cursor_is_placed_on_the_requested_row_and_column()
    {
        using var console = new FakeConsoleIO();
        var writer = new FrameWriter(console);

        writer.Paint(new RenderedFrame(["a", "b", "c"], 1, 4));

        Assert.EndsWith("\u001b[2A\r\u001b[4C", console.Writes[0]);
    }

    [Fact]
    public void Shrinking_frame_erases_the_rows_it_no_longer_uses()
    {
        using var console = new FakeConsoleIO();
        var writer = new FrameWriter(console);

        writer.Paint(new RenderedFrame(["a", "b", "> c"], 2, 3));
        writer.Paint(new RenderedFrame(["> d"], 0, 3));

        string second = console.Writes[1];
        Assert.StartsWith("\u001b[2A", second);
        Assert.Contains("\r\u001b[2K> d\n", second);
        Assert.Equal(3, CountOccurrences(second, "\r\u001b[2K"));
    }

    [Fact]
    public void Clear_erases_the_area_and_resets_the_height()
    {
        using var console = new FakeConsoleIO();
        var writer = new FrameWriter(console);

        writer.Paint(new RenderedFrame(["status", "> a"], 1, 3));
        writer.Clear();

        Assert.Equal("\u001b[1A\r\u001b[2K\n\r\u001b[2K\u001b[1A", console.Writes[1]);

        writer.Clear();

        Assert.Equal(2, console.Writes.Count);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (
            var i = haystack.IndexOf(needle, StringComparison.Ordinal);
            i >= 0;
            i = haystack.IndexOf(needle, i + 1, StringComparison.Ordinal)
        )
        {
            count++;
        }

        return count;
    }
}

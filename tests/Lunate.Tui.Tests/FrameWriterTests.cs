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
    public void Second_paint_moves_up_by_the_previous_height()
    {
        using var console = new FakeConsoleIO();
        var writer = new FrameWriter(console);

        writer.Paint(new RenderedFrame(["one", "two"], 0, 0));
        writer.Paint(new RenderedFrame(["three"], 0, 0));

        string second = console.Writes[1];
        Assert.StartsWith("\u001b[2A", second);
        Assert.Contains("\r\u001b[2Kthree\n", second);
        Assert.Contains("\r\u001b[2K\n", second);
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
    public void Clear_erases_the_area_and_resets_the_height()
    {
        using var console = new FakeConsoleIO();
        var writer = new FrameWriter(console);

        writer.Paint(new RenderedFrame(["one", "two"], 0, 0));
        writer.Clear();

        Assert.Equal("\u001b[2A\r\u001b[2K\n\r\u001b[2K\u001b[1A", console.Writes[1]);

        writer.Clear();

        Assert.Equal(2, console.Writes.Count);
    }
}

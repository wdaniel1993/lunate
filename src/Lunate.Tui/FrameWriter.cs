using System.Globalization;
using System.Text;

namespace Lunate.Tui;

internal sealed class FrameWriter
{
    private readonly IConsoleIO _console;
    private int _height;
    private int _cursorRow;

    public FrameWriter(IConsoleIO console) => _console = console;

    public void Paint(RenderedFrame frame)
    {
        var builder = new StringBuilder();

        // The cursor rests on the frame's cursor row (the input line), not below the frame:
        // moving up by the frame height would overshoot by (height - cursorRow) rows, drifting
        // the area upward and leaving unerased ghost rows behind on every repaint.
        if (_cursorRow > 0)
        {
            builder.Append(Escape(_cursorRow)).Append('A');
        }

        int rows = Math.Max(_height, frame.Lines.Count);
        for (var i = 0; i < rows; i++)
        {
            builder.Append('\r').Append("\u001b[2K");
            if (i < frame.Lines.Count)
            {
                builder.Append(frame.Lines[i]);
            }

            builder.Append('\n');
        }

        _height = rows;

        int cursorRow = Math.Clamp(frame.CursorRow, 0, Math.Max(0, rows - 1));
        int up = rows - cursorRow;
        if (up > 0)
        {
            builder.Append(Escape(up)).Append('A');
        }

        builder.Append('\r');
        if (frame.CursorColumn > 0)
        {
            builder.Append(Escape(frame.CursorColumn)).Append('C');
        }

        _cursorRow = cursorRow;
        _console.Write(builder.ToString());
    }

    public void Clear()
    {
        if (_height == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        if (_cursorRow > 0)
        {
            builder.Append(Escape(_cursorRow)).Append('A');
        }

        for (var i = 0; i < _height; i++)
        {
            builder.Append('\r').Append("\u001b[2K");
            if (i < _height - 1)
            {
                builder.Append('\n');
            }
        }

        if (_height > 1)
        {
            builder.Append(Escape(_height - 1)).Append('A');
        }

        _cursorRow = 0;
        _height = 0;
        _console.Write(builder.ToString());
    }

    /// <summary>
    /// Forgets the painted region without erasing it: the buffer those rows were painted on is
    /// being discarded wholesale (the alternate screen the full-screen mode leaves), so erasing on
    /// the current buffer would damage whatever stands there now.
    /// </summary>
    public void Forget()
    {
        _cursorRow = 0;
        _height = 0;
    }

    private static string Escape(int value) =>
        "\u001b[" + value.ToString(CultureInfo.InvariantCulture);
}

using System.Globalization;
using System.Text;

namespace Lunate.Tui;

internal sealed class FrameWriter
{
    private readonly IConsoleIO _console;
    private int _height;

    public FrameWriter(IConsoleIO console) => _console = console;

    public void Paint(RenderedFrame frame)
    {
        var builder = new StringBuilder();
        if (_height > 0)
        {
            builder.Append(Escape(_height)).Append('A');
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

        _console.Write(builder.ToString());
    }

    public void Clear()
    {
        if (_height == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.Append(Escape(_height)).Append('A');
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

        _console.Write(builder.ToString());
        _height = 0;
    }

    private static string Escape(int value) =>
        "\u001b[" + value.ToString(CultureInfo.InvariantCulture);
}

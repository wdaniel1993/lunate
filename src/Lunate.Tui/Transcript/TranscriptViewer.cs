using System.Text;
using Spectre.Console;

namespace Lunate.Tui;

/// <summary>
/// The transcript viewer's screen session: the alternate screen through the console seam, the
/// full-screen frame painted on every move, and the scroll offset the bindings drive. It is lazy
/// — nothing is entered or painted before <see cref="Open"/> — and every exit path (a closing
/// key, session end, a throwing paint) leaves the alternate screen.
/// </summary>
internal sealed class TranscriptViewer : IDisposable
{
    private readonly IConsoleIO _console;
    private readonly TranscriptViewerRenderer _renderer;
    private readonly StringWriter _buffer = new();
    private readonly IAnsiConsole _ansi;
    private IDisposable? _screen;
    private int _scrolledLines;

    public TranscriptViewer(
        IConsoleIO console,
        TerminalCapabilities capabilities,
        string? homeDirectory = null
    )
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(capabilities);
        _console = console;
        _renderer = new TranscriptViewerRenderer(capabilities, homeDirectory);
        // The frame's styled lines are markup; this console turns them into the ANSI the
        // alternate screen gets. The colour system adapts, so a colourless terminal emits none.
        _ansi = ConsoleColorSystem.Create(capabilities.Color, _buffer);
    }

    /// <summary>Whether the viewer owns the terminal right now.</summary>
    public bool IsOpen => _screen is not null;

    /// <summary>The offset from the latest content the frame is drawn at; 0 is the bottom.</summary>
    internal int ScrolledLines => _scrolledLines;

    /// <summary>Enters the alternate screen and paints the latest content.</summary>
    public void Open(TranscriptModel transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        if (IsOpen)
        {
            return;
        }

        _scrolledLines = 0;
        try
        {
            _screen = _console.EnterAlternateScreen();
            Paint(transcript);
        }
        catch
        {
            Close();
            throw;
        }
    }

    /// <summary>
    /// Handles one key while the viewer is open: a closing key (<c>Ctrl+O</c>, <c>q</c>,
    /// <c>Esc</c>) leaves the alternate screen; navigation moves the window and repaints. Returns
    /// false once the viewer closed, true while it stays open. Inert when it is not open.
    /// </summary>
    public bool HandleKey(KeyEvent key, TranscriptModel transcript)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(transcript);
        if (!IsOpen)
        {
            return false;
        }

        if (IsClose(key))
        {
            Close();
            return false;
        }

        if (Scroll(key, transcript) is { } scrolledLines)
        {
            _scrolledLines = scrolledLines;
            Paint(transcript);
        }

        return true;
    }

    /// <summary>Leaves the alternate screen; idempotent, so every exit path can call it.</summary>
    public void Close()
    {
        IDisposable? screen = _screen;
        _screen = null;
        _scrolledLines = 0;
        screen?.Dispose();
    }

    public void Dispose() => Close();

    private static bool IsClose(KeyEvent key) =>
        key.Kind == KeyKind.Escape
        || key is { Kind: KeyKind.Character, Ctrl: false, Alt: false, Text: "q" }
        || key is { Kind: KeyKind.Character, Ctrl: true, Alt: false, Text: "o" };

    /// <summary>
    /// The new scroll offset for a navigation key, clamped to the document; null for keys that do
    /// not move. Up (and PageUp) walk towards the oldest content, <c>g</c> and Home jump to the
    /// top, Down, <c>G</c> and End go back to the latest.
    /// </summary>
    private int? Scroll(KeyEvent key, TranscriptModel transcript)
    {
        int max = _renderer.MaxScrolledLines(transcript, _console.Size);
        return key switch
        {
            { Kind: KeyKind.Up } => Math.Min(_scrolledLines + 1, max),
            { Kind: KeyKind.Down } => Math.Max(_scrolledLines - 1, 0),
            { Kind: KeyKind.PageUp } => Math.Min(_scrolledLines + PageLines(), max),
            { Kind: KeyKind.PageDown } => Math.Max(_scrolledLines - PageLines(), 0),
            { Kind: KeyKind.Home } => max,
            { Kind: KeyKind.End } => 0,
            { Kind: KeyKind.Character, Ctrl: false, Alt: false, Text: "g" } => max,
            { Kind: KeyKind.Character, Ctrl: false, Alt: false, Text: "G" } => 0,
            _ => null,
        };
    }

    /// <summary>A page is the frame's content height: the rows above the footer.</summary>
    private int PageLines() => Math.Max(1, Math.Max(1, _console.Size.Rows) - 1);

    /// <summary>
    /// Paints one frame: home, then every line of the frame cleared and overwritten in place —
    /// exactly as many lines as the renderer produced, so the bottom never scrolls.
    /// </summary>
    private void Paint(TranscriptModel transcript)
    {
        ConsoleSize size = _console.Size;
        IReadOnlyList<StyledLine> lines = _renderer.Lines(transcript, size, _scrolledLines);
        _ansi.Profile.Width = Math.Max(1, size.Columns);

        var frame = new StringBuilder("\u001b[H");
        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                frame.Append('\n');
            }

            frame.Append('\r').Append("\u001b[2K").Append(Ansi(lines[index]));
        }

        _console.Write(frame.ToString());
    }

    private string Ansi(StyledLine line)
    {
        _buffer.GetStringBuilder().Clear();
        _ansi.Write(new Markup(line.ToMarkup()));
        return _buffer.ToString();
    }
}

using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The transcript viewer's screen session: lazy until opened, the alternate screen through the
/// console seam, the full-screen frame painted on every move, and every exit path — a closing
/// key, disposal, a throwing paint — restoring the primary screen.
/// </summary>
public sealed class TranscriptViewerTests
{
    private static readonly ConsoleSize Size = new(80, 10);

    [Fact]
    public void Nothing_is_painted_until_the_viewer_opens()
    {
        using var console = new FakeConsoleIO(Size);
        using var viewer = Viewer(console);

        Assert.False(viewer.IsOpen);
        Assert.Empty(console.Writes);
    }

    [Fact]
    public void Opening_enters_the_alternate_screen_and_paints_the_latest_content()
    {
        using var console = new FakeConsoleIO(Size);
        using var viewer = Viewer(console);

        viewer.Open(LongTranscript());

        Assert.True(viewer.IsOpen);
        Assert.True(console.InAlternateScreen);
        Assert.Equal("\u001b[?1049h", console.Writes[0]);
        Assert.Contains("step 30 finished", console.Writes[^1], StringComparison.Ordinal);
        Assert.Equal(0, viewer.ScrolledLines);
    }

    [Theory]
    [InlineData(KeyKind.Character, "q", false)]
    [InlineData(KeyKind.Escape, null, false)]
    [InlineData(KeyKind.Character, "o", true)]
    public void A_closing_key_leaves_the_alternate_screen(KeyKind kind, string? text, bool ctrl)
    {
        using var console = new FakeConsoleIO(Size);
        using var viewer = Viewer(console);
        viewer.Open(LongTranscript());

        bool staysOpen = viewer.HandleKey(Key(kind, text, ctrl: ctrl), LongTranscript());

        Assert.False(staysOpen);
        Assert.False(viewer.IsOpen);
        Assert.False(console.InAlternateScreen);
        Assert.Equal("\u001b[?1049l", console.Writes[^1]);
    }

    [Fact]
    public void An_ordinary_key_keeps_the_viewer_open()
    {
        using var console = new FakeConsoleIO(Size);
        using var viewer = Viewer(console);
        viewer.Open(LongTranscript());

        Assert.True(viewer.HandleKey(Key(KeyKind.Character, "x"), LongTranscript()));
        Assert.True(viewer.IsOpen);
        Assert.True(console.InAlternateScreen);
    }

    [Fact]
    public void Disposing_an_open_viewer_leaves_the_alternate_screen()
    {
        var console = new FakeConsoleIO(Size);
        var viewer = Viewer(console);
        viewer.Open(LongTranscript());

        viewer.Dispose();

        Assert.False(console.InAlternateScreen);
        Assert.Equal("\u001b[?1049l", console.Writes[^1]);
    }

    [Fact]
    public void A_throwing_paint_restores_the_screen()
    {
        var console = new ThrowingConsoleIO();
        using var viewer = Viewer(console);

        Assert.Throws<InvalidOperationException>(() => viewer.Open(LongTranscript()));

        Assert.False(console.InAlternateScreen);
        Assert.False(viewer.IsOpen);
    }

    [Fact]
    public void The_frame_paints_at_the_console_size()
    {
        using var console = new FakeConsoleIO(new ConsoleSize(20, 4));
        using var viewer = Viewer(console);

        viewer.Open(new TranscriptModel([new TranscriptUserEntry("hey")]));

        Assert.True(console.InAlternateScreen);
        Assert.Contains("hey", console.Writes[^1], StringComparison.Ordinal);
    }

    private static TranscriptViewer Viewer(IConsoleIO console) =>
        new(console, new TerminalCapabilities(ColorSystemSupport.NoColors, Unicode: true));

    private static KeyEvent Key(KeyKind kind, string? text = null, bool ctrl = false) =>
        new(kind, text, ctrl, false, false);

    private static TranscriptModel LongTranscript() =>
        new([
            new TranscriptToolEntry(
                new ToolBlockModel(
                    "bash",
                    """{"command":"build"}""",
                    ToolBlockStatus.Ok,
                    string.Join('\n', Enumerable.Range(1, 30).Select(n => $"step {n:00} finished")),
                    null
                )
            ),
        ]);

    /// <summary>A console whose frame writes fail: the terminal is gone mid-viewer.</summary>
    private sealed class ThrowingConsoleIO : IConsoleIO
    {
        public bool IsInteractive => false;

        public ConsoleSize Size => Size80;

        public IObservable<ConsoleSize> Resized => Observable.Empty<ConsoleSize>();

        public bool InAlternateScreen { get; private set; }

        public async IAsyncEnumerable<KeyEvent> ReadKeysAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            await Task.CompletedTask;
            yield break;
        }

        public void Write(string text)
        {
            if (text.Contains("\u001b[?1049h", StringComparison.Ordinal))
            {
                InAlternateScreen = true;
                return;
            }

            if (text.Contains("\u001b[?1049l", StringComparison.Ordinal))
            {
                InAlternateScreen = false;
                return;
            }

            throw new InvalidOperationException("the terminal is gone");
        }

        public IDisposable EnterRawMode() => new Noop();

        public IDisposable EnterAlternateScreen()
        {
            Write("\u001b[?1049h");
            return new AlternateScreenScope(this);
        }

        private static ConsoleSize Size80 => new(80, 24);

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }

        private sealed class AlternateScreenScope(ThrowingConsoleIO console) : IDisposable
        {
            private bool _left;

            public void Dispose()
            {
                if (_left)
                {
                    return;
                }

                _left = true;
                console.Write("\u001b[?1049l");
            }
        }
    }
}

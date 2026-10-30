using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>
/// The full-screen mode: an opt-in alternate screen for the whole session. The inline session is
/// the default; <c>--fullscreen</c> or <c>tui.fullscreen: true</c> engages the mode, which enters
/// the alternate screen through the <see cref="IConsoleIO"/> seam after the first frame is painted
/// and leaves it on every exit path — a normal quit, <c>Ctrl+C</c>, <c>Ctrl+D</c>, an unhandled
/// exception, session disposal. No new key: the quit window that ends the inline session ends the
/// full-screen one, and leaving restores the terminal's scrollback exactly as it was.
/// </summary>
internal sealed partial class InteractiveSession
{
    /// <summary>The full-screen scope; null while the session renders inline.</summary>
    private IDisposable? _fullScreen;

    /// <summary>
    /// Enters the alternate screen when the mode is on; idempotent, and fail soft like raw mode —
    /// a terminal that cannot switch buffers keeps the session usable inline and says so instead
    /// of ending it.
    /// </summary>
    private void EnterFullScreen()
    {
        if (_fullScreen is not null || !_settings.Tui.Fullscreen)
        {
            return;
        }

        // The frame rows on the buffer about to be left are erased before the switch, so the
        // terminal the mode hands back when it ends is exactly the scrollback it was handed.
        _live.SwitchScreen();
        try
        {
            _fullScreen = _console.EnterAlternateScreen();
        }
        catch (Exception exception)
        {
            // The buffer never changed: repaint the frame over the erased region and continue on
            // the primary screen. The mode stays on for a later chance (the transcript viewer's
            // bracket calls back in), but nothing this session does then depends on the switch.
            _live.Redraw();
            Notify($"full screen unavailable: {exception.Message}", NoticeLevel.Warning);
        }
    }

    /// <summary>Leaves the alternate screen; idempotent, so every exit path can call it.</summary>
    private void LeaveFullScreen()
    {
        IDisposable? screen = _fullScreen;
        _fullScreen = null;
        if (screen is null)
        {
            return;
        }

        screen.Dispose();
        // The frame's rows lived on the discarded buffer; the next paint must not reach back into
        // the scrollback the terminal just restored.
        _live.ForgetFrame();
    }
}

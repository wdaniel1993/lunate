namespace Lunate.Tui;

public interface IConsoleIO
{
    bool IsInteractive { get; }

    ConsoleSize Size { get; }

    IObservable<ConsoleSize> Resized { get; }

    IAsyncEnumerable<KeyEvent> ReadKeysAsync(CancellationToken cancellationToken);

    void Write(string text);

    IDisposable EnterRawMode();

    /// <summary>
    /// Hands the terminal to a child process: the key pump stops reading, the terminal returns to
    /// the mode the process started with (cooked), and the decoder's partial sequence is dropped.
    /// Disposing the scope re-enters raw mode, restarts the pump and drops whatever partial
    /// sequence arrived meanwhile. A session must dispose the scope on every exit path.
    /// </summary>
    /// <returns>The scope that resumes the session's terminal when disposed.</returns>
    IDisposable SuspendTerminal();

    /// <summary>
    /// Enters the alternate screen (the transcript viewer's full-screen surface); disposing the
    /// returned scope leaves it and brings the primary screen back. A session must dispose the
    /// scope on every exit path, exceptions included.
    /// </summary>
    /// <returns>The scope that leaves the alternate screen when disposed.</returns>
    IDisposable EnterAlternateScreen();
}

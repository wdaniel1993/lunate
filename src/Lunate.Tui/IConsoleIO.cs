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
    /// Enters the alternate screen (the transcript viewer's full-screen surface); disposing the
    /// returned scope leaves it and brings the primary screen back. A session must dispose the
    /// scope on every exit path, exceptions included.
    /// </summary>
    /// <returns>The scope that leaves the alternate screen when disposed.</returns>
    IDisposable EnterAlternateScreen();
}

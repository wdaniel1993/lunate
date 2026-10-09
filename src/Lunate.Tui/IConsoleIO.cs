namespace Lunate.Tui;

public interface IConsoleIO
{
    bool IsInteractive { get; }

    ConsoleSize Size { get; }

    IObservable<ConsoleSize> Resized { get; }

    IAsyncEnumerable<KeyEvent> ReadKeysAsync(CancellationToken cancellationToken);

    void Write(string text);

    IDisposable EnterRawMode();
}

using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;

namespace Lunate.Tui;

internal sealed class FakeConsoleIO : IConsoleIO, IDisposable
{
    private readonly Subject<ConsoleSize> _resized = new();
    private readonly IObservable<ConsoleSize> _distinctSizes;
    private readonly List<KeyEvent> _keys = [];
    private readonly List<string> _writes = [];
    private bool _rawMode;

    public FakeConsoleIO(ConsoleSize size)
    {
        Size = size;
        _distinctSizes = _resized.DistinctUntilChanged();
    }

    public FakeConsoleIO()
        : this(new ConsoleSize(80, 24)) { }

    public bool IsInteractive { get; set; } = true;

    public ConsoleSize Size { get; private set; }

    public IObservable<ConsoleSize> Resized => _distinctSizes;

    public IReadOnlyList<string> Writes => _writes;

    public IReadOnlyList<KeyEvent> Keys => _keys;

    public bool InRawMode => _rawMode;

    public int RawModeEntries { get; private set; }

    public void SetSize(ConsoleSize size) => Size = size;

    public void PushResize(ConsoleSize size)
    {
        Size = size;
        _resized.OnNext(size);
    }

    public void EnqueueKey(KeyEvent key) => _keys.Add(key);

    public void EnqueueKeys(params KeyEvent[] keys) => _keys.AddRange(keys);

    public void Write(string text) => _writes.Add(text);

    public IDisposable EnterRawMode()
    {
        RawModeEntries++;
        _rawMode = true;
        return new RawMode(this);
    }

    public async IAsyncEnumerable<KeyEvent> ReadKeysAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        foreach (var key in _keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return key;
        }

        await Task.CompletedTask;
    }

    public void Dispose() => _resized.Dispose();

    private sealed class RawMode(FakeConsoleIO console) : IDisposable
    {
        public void Dispose() => console._rawMode = false;
    }
}

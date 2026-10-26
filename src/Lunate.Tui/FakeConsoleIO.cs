using System.Reactive.Concurrency;
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
    private readonly List<string> _typedAtChild = [];
    private readonly List<string> _terminalTransitions = [];
    private readonly KeyReader? _byteReader;
    private bool _rawMode;
    private bool _suspended;

    public FakeConsoleIO(ConsoleSize size)
    {
        Size = size;
        _distinctSizes = _resized.DistinctUntilChanged();
    }

    public FakeConsoleIO()
        : this(new ConsoleSize(80, 24)) { }

    public FakeConsoleIO(IScheduler scheduler, ConsoleSize size)
        : this(size)
    {
        _byteReader = new KeyReader(scheduler, _keys.Add);
    }

    public FakeConsoleIO(IScheduler scheduler)
        : this(scheduler, new ConsoleSize(80, 24)) { }

    public bool IsInteractive { get; set; } = true;

    public ConsoleSize Size { get; private set; }

    public IObservable<ConsoleSize> Resized => _distinctSizes;

    public IReadOnlyList<string> Writes => _writes;

    public IReadOnlyList<KeyEvent> Keys => _keys;

    /// <summary>Keys typed while a child owned the terminal: they never reached the reader.</summary>
    public IReadOnlyList<string> TypedAtChild => _typedAtChild;

    /// <summary>
    /// The suspend/resume transitions in order, each with the input mode at that point
    /// (<c>suspend:cooked</c>, <c>resume:raw</c>), so a test can assert the child saw a cooked
    /// terminal and that the session's read loop was quiesced around it.
    /// </summary>
    public IReadOnlyList<string> TerminalTransitions => _terminalTransitions;

    /// <summary>How many times the terminal was handed to a child.</summary>
    public int SuspendCount { get; private set; }

    /// <summary>True while a child owns the terminal.</summary>
    public bool IsTerminalSuspended => _suspended;

    public bool InRawMode => _rawMode;

    public int RawModeEntries { get; private set; }

    public void SetSize(ConsoleSize size) => Size = size;

    public void PushResize(ConsoleSize size)
    {
        Size = size;
        _resized.OnNext(size);
    }

    public void EnqueueKey(KeyEvent key)
    {
        if (_suspended)
        {
            _typedAtChild.Add(key.Text ?? key.Kind.ToString());
            return;
        }

        _keys.Add(key);
    }

    public void EnqueueKeys(params KeyEvent[] keys)
    {
        foreach (KeyEvent key in keys)
        {
            EnqueueKey(key);
        }
    }

    public void FeedBytes(params byte[] bytes)
    {
        if (_byteReader is null)
        {
            throw new InvalidOperationException(
                "FakeConsoleIO byte feeding needs a scheduler; use the scheduler constructor."
            );
        }

        _byteReader.Feed(bytes);
    }

    public void Write(string text) => _writes.Add(text);

    public IDisposable EnterRawMode()
    {
        RawModeEntries++;
        _rawMode = true;
        return new RawMode(this);
    }

    public IDisposable EnterAlternateScreen()
    {
        AlternateScreenEntries++;
        InAlternateScreen = true;
        return new AlternateScreenScope(this, AlternateScreen.Enter(Write));
    }

    /// <summary>
    /// Scripts the terminal handover: the fake goes cooked, records the transition, and any key
    /// enqueued while the scope is held goes to <see cref="TypedAtChild"/> instead of the reader.
    /// </summary>
    public IDisposable SuspendTerminal()
    {
        SuspendCount++;
        _suspended = true;
        _rawMode = false;
        _terminalTransitions.Add("suspend:cooked");
        return new Suspension(this);
    }

    /// <summary>Whether the alternate screen is entered right now.</summary>
    public bool InAlternateScreen { get; private set; }

    /// <summary>How many times the alternate screen was entered.</summary>
    public int AlternateScreenEntries { get; private set; }

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

    public void Dispose()
    {
        _byteReader?.Dispose();
        _resized.Dispose();
    }

    private sealed class RawMode(FakeConsoleIO console) : IDisposable
    {
        public void Dispose() => console._rawMode = false;
    }

    private sealed class AlternateScreenScope(FakeConsoleIO console, IDisposable screen)
        : IDisposable
    {
        public void Dispose()
        {
            screen.Dispose();
            console.InAlternateScreen = false;
        }
    }

    private sealed class Suspension(FakeConsoleIO console) : IDisposable
    {
        private bool _resumed;

        public void Dispose()
        {
            if (_resumed)
            {
                return;
            }

            _resumed = true;
            console._suspended = false;
            console._rawMode = true;
            console._terminalTransitions.Add("resume:raw");
        }
    }
}

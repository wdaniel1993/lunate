using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Lunate.Tui.Platform;

namespace Lunate.Tui;

internal sealed class SystemConsoleIO : IConsoleIO
{
    internal static readonly TimeSpan ResizePollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IScheduler _scheduler;
    private readonly Channel<KeyEvent> _keys = Channel.CreateUnbounded<KeyEvent>(
        new UnboundedChannelOptions { SingleReader = true }
    );

    /// <summary>Held by the pump across one readiness wait and its read, so a suspend can bar reads.</summary>
    private readonly SemaphoreSlim _readGate = new(1, 1);

    /// <summary>Set while the terminal belongs to a child; the pump parks on it instead of reading.</summary>
    private readonly ManualResetEventSlim _resumed = new(initialState: true);

    private volatile bool _suspended;
    private KeyReader? _reader;
    private ITerminalMode? _terminalMode;

    public SystemConsoleIO(IScheduler scheduler)
    {
        _scheduler = scheduler;
        IsInteractive = ComputeIsInteractive(
            Console.IsInputRedirected,
            Console.IsOutputRedirected,
            Environment.GetEnvironmentVariable("TERM"),
            OperatingSystem.IsWindows(),
            OperatingSystem.IsWindows() && WindowsConsoleMode.IsConsoleAttached()
        );
    }

    public bool IsInteractive { get; }

    public ConsoleSize Size => new(Console.WindowWidth, Console.WindowHeight);

    public IObservable<ConsoleSize> Resized => PollSize(() => Size, ResizePollInterval, _scheduler);

    public void Write(string text)
    {
        Console.Out.Write(text);
        Console.Out.Flush();
    }

    public async IAsyncEnumerable<KeyEvent> ReadKeysAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        using var reader = new KeyReader(_scheduler, key => _keys.Writer.TryWrite(key));
        _reader = reader;
        var pump = PumpBytesAsync(reader, cancellationToken);
        try
        {
            await foreach (var key in _keys.Reader.ReadAllAsync(cancellationToken))
            {
                yield return key;
            }
        }
        finally
        {
            _reader = null;
            await pump.ConfigureAwait(false);
        }
    }

    public IDisposable EnterRawMode()
    {
        if (!IsInteractive)
        {
            return NoopDisposable.Instance;
        }

        _terminalMode = OperatingSystem.IsWindows()
            ? WindowsConsoleMode.Enter()
            : UnixRawMode.Enter();
        return _terminalMode;
    }

    /// <summary>
    /// Quiesces the pump, returns the terminal to the mode it had before raw mode, and drops a
    /// partial sequence the decoder still held. The key channel stays open: one read loop, one
    /// lifetime, with only its blocking read set aside.
    /// </summary>
    public IDisposable SuspendTerminal()
    {
        if (!IsInteractive || _terminalMode is not { } mode)
        {
            return NoopDisposable.Instance;
        }

        // Taking the gate waits for at most one in-flight read; setting the flag under it
        // guarantees no later read starts.
        _resumed.Reset();
        _readGate.Wait();
        try
        {
            _suspended = true;
        }
        finally
        {
            _readGate.Release();
        }

        try
        {
            mode.Suspend();
        }
        catch
        {
            ResumeTerminal();
            throw;
        }

        _reader?.FlushPartial();
        return new Suspension(this);
    }

    private void ResumeTerminal()
    {
        _reader?.FlushPartial();
        _terminalMode?.Resume();
        _readGate.Wait();
        try
        {
            _suspended = false;
        }
        finally
        {
            _readGate.Release();
        }

        _resumed.Set();
    }

    private sealed class Suspension(SystemConsoleIO console) : IDisposable
    {
        private bool _resumed;

        public void Dispose()
        {
            if (_resumed)
            {
                return;
            }

            _resumed = true;
            console.ResumeTerminal();
        }
    }

    /// <summary>Enters the alternate screen; disposing the scope restores the primary screen.</summary>
    public IDisposable EnterAlternateScreen() => AlternateScreen.Enter(Write);

    internal static bool ComputeIsInteractive(
        bool inputRedirected,
        bool outputRedirected,
        string? term,
        bool isWindows,
        bool windowsConsoleAttached
    ) =>
        !inputRedirected
        && !outputRedirected
        && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase)
        && (!isWindows || windowsConsoleAttached);

    internal static IObservable<ConsoleSize> PollSize(
        Func<ConsoleSize> read,
        TimeSpan interval,
        IScheduler scheduler
    ) => Observable.Interval(interval, scheduler).Select(_ => read()).DistinctUntilChanged();

    private const int PollIntervalMs = 200;

    /// <summary>
    /// Reads stdin into the key reader, one readiness-checked read at a time, and parks while the
    /// terminal belongs to a child. Raw descriptor reads, never the runtime's console stream: on
    /// Unix the console stack re-applies its own terminal mode around console operations, which
    /// silently overwrites the session's raw mode after it is entered (Enter then arrives as LF and
    /// can never submit).
    /// </summary>
    private async Task PumpBytesAsync(KeyReader reader, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var input = Console.OpenStandardInput();
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!AwaitInput())
                    {
                        continue;
                    }

                    await _readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (_suspended || cancellationToken.IsCancellationRequested)
                        {
                            continue;
                        }

                        int read = await input
                            .ReadAsync(buffer, cancellationToken)
                            .ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        reader.Feed(buffer.AsSpan(0, read));
                    }
                    finally
                    {
                        _readGate.Release();
                    }
                }
            }
            else
            {
                await Task.Run(
                        () =>
                        {
                            while (!cancellationToken.IsCancellationRequested)
                            {
                                if (!AwaitInput())
                                {
                                    continue;
                                }

                                _readGate.Wait();
                                try
                                {
                                    if (_suspended || cancellationToken.IsCancellationRequested)
                                    {
                                        continue;
                                    }

                                    nint read = PosixRead(0, buffer, (nuint)buffer.Length);
                                    if (read <= 0)
                                    {
                                        return;
                                    }
                                    else
                                    {
                                        reader.Feed(buffer.AsSpan(0, (int)read));
                                    }
                                }
                                finally
                                {
                                    _readGate.Release();
                                }
                            }
                        },
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _keys.Writer.TryComplete();
        }
    }

    /// <summary>True when stdin has bytes; parks while suspended and reports false on a timeout.</summary>
    private bool AwaitInput()
    {
        if (_suspended)
        {
            _resumed.Wait(PollIntervalMs);
            return false;
        }

        return ConsoleInputReadiness.IsReady(PollIntervalMs);
    }

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint PosixRead(int fd, byte[] buffer, nuint count);
}

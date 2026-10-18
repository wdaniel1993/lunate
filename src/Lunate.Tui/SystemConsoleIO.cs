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
            await pump.ConfigureAwait(false);
        }
    }

    public IDisposable EnterRawMode()
    {
        if (!IsInteractive)
        {
            return NoopDisposable.Instance;
        }

        return OperatingSystem.IsWindows() ? WindowsConsoleMode.Enter() : UnixRawMode.Enter();
    }

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

    private const short PollIn = 0x0001;
    private const int PollIntervalMs = 200;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;

        public short Events;

        public short Revents;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int poll([In, Out] PollFd[] fds, uint nfds, int timeoutMs);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint PosixRead(int fd, byte[] buffer, nuint count);

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
                    int read = await input
                        .ReadAsync(buffer, cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    reader.Feed(buffer.AsSpan(0, read));
                }
            }
            else
            {
                // Raw descriptor reads, never the runtime's console stream: on Unix the console
                // stack re-applies its own terminal mode around console operations, which
                // silently overwrites the session's raw mode after it is entered (Enter then
                // arrives as LF and can never submit). poll() bounds the cancellation latency
                // to one interval.
                await Task.Run(
                        () =>
                        {
                            PollFd[] fds = [new() { Fd = 0, Events = PollIn }];
                            while (!cancellationToken.IsCancellationRequested)
                            {
                                int ready = poll(fds, 1, PollIntervalMs);
                                if (ready < 0)
                                {
                                    break;
                                }

                                if (ready == 0)
                                {
                                    continue;
                                }

                                nint read = PosixRead(0, buffer, (nuint)buffer.Length);
                                if (read <= 0)
                                {
                                    break;
                                }

                                reader.Feed(buffer.AsSpan(0, (int)read));
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
}

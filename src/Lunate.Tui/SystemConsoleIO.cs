using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
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

    private async Task PumpBytesAsync(KeyReader reader, CancellationToken cancellationToken)
    {
        var input = Console.OpenStandardInput();
        var buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                reader.Feed(buffer.AsSpan(0, read));
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _keys.Writer.TryComplete();
        }
    }
}

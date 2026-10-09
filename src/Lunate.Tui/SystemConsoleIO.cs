using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Channels;
using Lunate.Tui.Platform;

namespace Lunate.Tui;

internal sealed class SystemConsoleIO : IConsoleIO
{
    internal static readonly TimeSpan ResizePollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IScheduler _scheduler;

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

    public IAsyncEnumerable<KeyEvent> ReadKeysAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("raw key reading is wired with KeyReader in task 2.2.");

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
}

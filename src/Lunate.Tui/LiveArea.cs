using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Lunate.Tui;

internal sealed class LiveArea : IDisposable
{
    internal static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);
    internal static readonly TimeSpan SpinnerInterval = TimeSpan.FromMilliseconds(120);

    private readonly IConsoleIO _console;
    private readonly IScheduler _scheduler;
    private readonly Subject<LiveAreaInput> _stimuli = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly FrameWriter _writer;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _postGate = new();
    private bool _started;
    private bool _disposed;

    public LiveArea(IConsoleIO console, IScheduler scheduler)
    {
        _console = console;
        _scheduler = scheduler;
        _writer = new FrameWriter(console);
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        var initial = new LiveAreaState { Size = _console.Size };
        _writer.Paint(LiveAreaRenderer.Render(initial));

        var states = _stimuli
            .Scan(initial, Reduce)
            .StartWith(initial)
            .DistinctUntilChanged()
            .Skip(1);
        _subscriptions.Add(
            states
                .Sample(FrameInterval, _scheduler)
                .Subscribe(state => _writer.Paint(LiveAreaRenderer.Render(state)))
        );
        _subscriptions.Add(_console.Resized.Subscribe(size => Post(new ResizeInput(size))));
        _subscriptions.Add(
            Observable.Interval(SpinnerInterval, _scheduler).Subscribe(_ => Post(new SpinnerTick()))
        );
        _ = PumpKeysAsync(_cts.Token);
    }

    public void PostKey(KeyEvent key) => Post(new KeyInput(key));

    public void AppendTail(string text) => Post(new TailInput(text));

    public void SetTool(string? toolName) => Post(new ToolInput(toolName));

    public void SetFooter(
        string? model,
        long inputTokens,
        long outputTokens,
        double contextPercent,
        string? workingDirectory,
        string? gitBranch
    ) =>
        Post(
            new FooterInput(
                model,
                inputTokens,
                outputTokens,
                contextPercent,
                workingDirectory,
                gitBranch
            )
        );

    public void Dispose()
    {
        lock (_postGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _cts.Cancel();
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _stimuli.Dispose();

        // The render subscription is gone, so queue the final clear on the same
        // scheduler that paints frames; the terminating thread never touches
        // FrameWriter state concurrently. Dispose the CTS only after the pump has
        // been cancelled, which keeps its token valid for the in-flight reads.
        _scheduler.Schedule(() => _writer.Clear());
        _cts.Dispose();
    }

    private void Post(LiveAreaInput input)
    {
        lock (_postGate)
        {
            if (_disposed)
            {
                return;
            }

            _stimuli.OnNext(input);
        }
    }

    private async Task PumpKeysAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var key in _console.ReadKeysAsync(cancellationToken))
            {
                PostKey(key);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static LiveAreaState Reduce(LiveAreaState state, LiveAreaInput input) =>
        input switch
        {
            KeyInput key => state with { Input = InputLine.Apply(state.Input, key.Key) },
            ResizeInput resize => state with { Size = resize.Size },
            SpinnerTick => state.ToolName is null
                ? state
                : state with
                {
                    FrameNumber = state.FrameNumber + 1,
                },
            TailInput tail => state with { TailText = state.TailText + tail.Text },
            ToolInput tool => state with { ToolName = tool.Name, FrameNumber = 0 },
            FooterInput footer => state with
            {
                Model = footer.Model,
                InputTokens = footer.InputTokens,
                OutputTokens = footer.OutputTokens,
                ContextPercent = footer.ContextPercent,
                WorkingDirectory = footer.WorkingDirectory,
                GitBranch = footer.GitBranch,
            },
            _ => state,
        };

    private abstract record LiveAreaInput;

    private sealed record KeyInput(KeyEvent Key) : LiveAreaInput;

    private sealed record ResizeInput(ConsoleSize Size) : LiveAreaInput;

    private sealed record SpinnerTick : LiveAreaInput;

    private sealed record TailInput(string Text) : LiveAreaInput;

    private sealed record ToolInput(string? Name) : LiveAreaInput;

    private sealed record FooterInput(
        string? Model,
        long InputTokens,
        long OutputTokens,
        double ContextPercent,
        string? WorkingDirectory,
        string? GitBranch
    ) : LiveAreaInput;
}

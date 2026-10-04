using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Reactive.Testing;
using S5.Harness;

namespace S5.VariantB;

/// <summary>
/// Variant B — System.Reactive. Events, keys and resizes are merged into one
/// observable and folded with <c>Scan</c>; a <c>Sample</c> over the state
/// stream caps renders at ~30 fps, and an interval keeps the spinner alive
/// while a tool runs. Tests use <see cref="TestScheduler"/> virtual time.
/// </summary>
public sealed class VariantBSession : ISession
{
    private readonly TestScheduler _scheduler;
    private readonly FakeTerminal _terminal;
    private readonly LiveAreaState _state = new();
    private readonly Subject<LiveInput> _inputs = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly DateTimeOffset _start;
    private bool _disposed;

    public VariantBSession(TestScheduler scheduler, FakeTerminal terminal)
    {
        _scheduler = scheduler;
        _terminal = terminal;
        _start = scheduler.Now;

        var states = _inputs
            .Scan(_state, (state, input) =>
            {
                state.Apply(input);
                return state;
            })
            .Replay(1)
            .RefCount();

        _subscriptions.Add(states.Subscribe(_ => { }));
        _subscriptions.Add(_inputs.OfType<LiveInput.Event>()
            .Select(e => e.Value)
            .OfType<AgentEvent.ToolFinished>()
            .Subscribe(finished => _terminal.WriteBlock(SpectreBlocks.ToolResult(finished))));

        var pulse = states
            .Select(_ => 0L)
            .Merge(Observable.Interval(Scenario.FrameInterval, scheduler)
                .Where(_ => _state.ToolRunning || _state.PendingApprovalText is not null)
                .Select(_ => 0L))
            .Sample(Scenario.FrameInterval, scheduler);

        _subscriptions.Add(pulse.Subscribe(_ =>
        {
            if (_state.Apply(new LiveInput.Frame()))
            {
                _terminal.Render(Snapshot());
            }
        }));
    }

    public FakeTerminal Terminal => _terminal;

    public bool IsRunning => _state.Running;

    public bool IsToolRunning => _state.ToolRunning;

    public bool IsApprovalPending => _state.PendingApprovalText is not null;

    public bool IsCancelRequested => _state.CancelRequested;

    public bool IsQuitRequested => _state.QuitRequested;

    public string TailText => _state.TailText;

    public void Post(AgentEvent value) => _inputs.OnNext(new LiveInput.Event(value));

    public void Key(ConsoleKeyInfo key) => _inputs.OnNext(new LiveInput.Key(key, _scheduler.Now - _start));

    public void Resize(int width, int height)
    {
        _terminal.Resize(width, height);
        _inputs.OnNext(new LiveInput.Resize());
    }

    public void Advance(TimeSpan delta) => _scheduler.AdvanceBy(delta.Ticks);

    public ValueTask DrainAsync() => ValueTask.CompletedTask;

    public IReadOnlyList<string> Snapshot() =>
        LiveAreaRenderer.Render(
            _state.Capture(Spinner.Glyph(_state.ToolRunning, _state.FrameNumber)),
            _terminal.Width);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _inputs.Dispose();
    }
}

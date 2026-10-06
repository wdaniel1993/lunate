using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Reactive.Testing;
using ReactiveUI;
using S5.Harness;

namespace S5.VariantBPlus;

/// <summary>
/// Variant B+ — the variant B pipeline plus ReactiveUI view models for the
/// status footer and the approval prompt. A render function subscribes to the
/// view models; keys execute approval commands. No ReactiveUI view bindings.
/// </summary>
public sealed class VariantBPlusSession : ISession
{
    private readonly TestScheduler _scheduler;
    private readonly FakeTerminal _terminal;
    private readonly LiveAreaState _state = new();
    private readonly Subject<LiveInput> _inputs = new();
    private readonly StatusFooterViewModel _footer = new();
    private readonly ApprovalViewModel _approval;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly DateTimeOffset _start;
    private bool _disposed;

    public VariantBPlusSession(TestScheduler scheduler, FakeTerminal terminal)
    {
        _scheduler = scheduler;
        _terminal = terminal;
        _start = scheduler.Now;
        _approval = new ApprovalViewModel(decision =>
            _inputs.OnNext(new LiveInput.Approval(decision))
        );

        var states = _inputs
            .Scan(
                _state,
                (state, input) =>
                {
                    state.Apply(input);
                    return state;
                }
            )
            .Replay(1)
            .RefCount();

        _subscriptions.Add(states.Subscribe(SyncViewModels));
        _subscriptions.Add(
            _inputs
                .OfType<LiveInput.Event>()
                .Select(e => e.Value)
                .OfType<AgentEvent.ToolFinished>()
                .Subscribe(finished => _terminal.WriteBlock(SpectreBlocks.ToolResult(finished)))
        );

        _subscriptions.Add(
            _footer
                .Changed.Where(args => args.PropertyName == nameof(StatusFooterViewModel.Text))
                .Subscribe(_ => RequestRender())
        );
        _subscriptions.Add(
            _approval
                .Changed.Where(args => args.PropertyName == nameof(ApprovalViewModel.Prompt))
                .Subscribe(_ => RequestRender())
        );

        var pulse = states
            .Select(_ => 0L)
            .Merge(
                Observable
                    .Interval(Scenario.FrameInterval, scheduler)
                    .Where(_ => _state.ToolRunning || _state.PendingApprovalText is not null)
                    .Select(_ => 0L)
            )
            .Sample(Scenario.FrameInterval, scheduler);

        _subscriptions.Add(pulse.Subscribe(_ => RequestRender()));
    }

    public FakeTerminal Terminal => _terminal;

    public bool IsRunning => _state.Running;

    public bool IsToolRunning => _state.ToolRunning;

    public bool IsApprovalPending => _state.PendingApprovalText is not null;

    public bool IsCancelRequested => _state.CancelRequested;

    public bool IsQuitRequested => _state.QuitRequested;

    public string TailText => _state.TailText;

    public void Post(AgentEvent value) => _inputs.OnNext(new LiveInput.Event(value));

    public void Key(ConsoleKeyInfo key)
    {
        if (IsApprovalPending && _approval.TryResolve(key))
        {
            return;
        }

        _inputs.OnNext(new LiveInput.Key(key, _scheduler.Now - _start));
    }

    public void Resize(int width, int height)
    {
        _terminal.Resize(width, height);
        _inputs.OnNext(new LiveInput.Resize());
    }

    public void Advance(TimeSpan delta) => _scheduler.AdvanceBy(delta.Ticks);

    public ValueTask DrainAsync() => ValueTask.CompletedTask;

    public IReadOnlyList<string> Snapshot()
    {
        var view = _state.Capture(Spinner.Glyph(_state.ToolRunning, _state.FrameNumber)) with
        {
            Footer = _footer.Text,
            ApprovalPrompt = _approval.Prompt,
        };
        return LiveAreaRenderer.Render(view, _terminal.Width);
    }

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

    private void SyncViewModels(LiveAreaState state)
    {
        _footer.Model = state.Model;
        _footer.InputTokens = state.InputTokens;
        _footer.OutputTokens = state.OutputTokens;
        _footer.ContextPercent = state.ContextPercent;
        _approval.Sync(state.PendingApprovalText);
    }

    private void RequestRender()
    {
        if (_state.Apply(new LiveInput.Frame()))
        {
            _terminal.Render(Snapshot());
        }
    }
}

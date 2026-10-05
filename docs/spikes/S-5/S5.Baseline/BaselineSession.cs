using S5.Harness;

namespace S5.Baseline;

/// <summary>
/// The measurement baseline: same shared state machine and renderer, but no
/// concurrency model at all — inputs are applied synchronously and every
/// advance paints. Variant startup/memory numbers are deltas against this.
/// </summary>
public sealed class BaselineSession : ISession
{
    private readonly FakeTerminal _terminal;
    private readonly LiveAreaState _state = new();
    private TimeSpan _now;

    public BaselineSession(FakeTerminal terminal) => _terminal = terminal;

    public FakeTerminal Terminal => _terminal;

    public bool IsRunning => _state.Running;

    public bool IsToolRunning => _state.ToolRunning;

    public bool IsApprovalPending => _state.PendingApprovalText is not null;

    public bool IsCancelRequested => _state.CancelRequested;

    public bool IsQuitRequested => _state.QuitRequested;

    public string TailText => _state.TailText;

    public void Post(AgentEvent value) => Apply(new LiveInput.Event(value));

    public void Key(ConsoleKeyInfo key) => Apply(new LiveInput.Key(key, _now));

    public void Resize(int width, int height)
    {
        _terminal.Resize(width, height);
        _state.Apply(new LiveInput.Resize());
    }

    public void Advance(TimeSpan delta)
    {
        _now += delta;
        _state.Apply(new LiveInput.Frame());
        _terminal.Render(Snapshot());
    }

    public ValueTask DrainAsync() => ValueTask.CompletedTask;

    public IReadOnlyList<string> Snapshot() =>
        LiveAreaRenderer.Render(
            _state.Capture(Spinner.Glyph(_state.ToolRunning, _state.FrameNumber)),
            _terminal.Width
        );

    public void Dispose() { }

    private void Apply(LiveInput input)
    {
        if (input is LiveInput.Event { Value: AgentEvent.ToolFinished finished })
        {
            _terminal.WriteBlock(SpectreBlocks.ToolResult(finished));
        }

        _state.Apply(input);
    }
}

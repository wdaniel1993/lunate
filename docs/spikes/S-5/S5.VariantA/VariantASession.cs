using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using S5.Harness;

namespace S5.VariantA;

/// <summary>
/// Variant A — plain async. Event, key, resize and frame-clock producers all
/// write into one <see cref="Channel{T}"/>; a single consumer loop applies them.
/// The frame clock is a <see cref="TimeProvider"/> timer, so tests get virtual
/// time through <see cref="FakeTimeProvider"/>.
/// </summary>
public sealed class VariantASession : ISession
{
    private readonly FakeTimeProvider _time;
    private readonly FakeTerminal _terminal;
    private readonly LiveAreaState _state = new();
    private readonly Channel<LiveInput> _input = Channel.CreateUnbounded<LiveInput>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly ITimer _frameClock;
    private readonly Task _loop;
    private readonly DateTimeOffset _start;
    private bool _disposed;

    public VariantASession(FakeTimeProvider time, FakeTerminal terminal)
    {
        _time = time;
        _terminal = terminal;
        _start = time.GetUtcNow();
        _frameClock = time.CreateTimer(
            _ => _input.Writer.TryWrite(new LiveInput.Frame()),
            state: null,
            dueTime: Scenario.FrameInterval,
            period: Scenario.FrameInterval);
        _loop = Task.Run(RunAsync);
    }

    public FakeTerminal Terminal => _terminal;

    public bool IsRunning => _state.Running;

    public bool IsToolRunning => _state.ToolRunning;

    public bool IsApprovalPending => _state.PendingApprovalText is not null;

    public bool IsCancelRequested => _state.CancelRequested;

    public bool IsQuitRequested => _state.QuitRequested;

    public string TailText => _state.TailText;

    public void Post(AgentEvent value) => _input.Writer.TryWrite(new LiveInput.Event(value));

    public void Key(ConsoleKeyInfo key) => _input.Writer.TryWrite(new LiveInput.Key(key, Now));

    public void Resize(int width, int height)
    {
        _terminal.Resize(width, height);
        _input.Writer.TryWrite(new LiveInput.Resize());
    }

    public void Advance(TimeSpan delta) => _time.Advance(delta);

    public ValueTask DrainAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_input.Writer.TryWrite(new LiveInput.Drain(completion)))
        {
            completion.TrySetResult();
        }

        return new ValueTask(completion.Task);
    }

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
        _cts.Cancel();
        _input.Writer.TryComplete();
        _frameClock.Dispose();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
    }

    private TimeSpan Now => _time.GetUtcNow() - _start;

    private async Task RunAsync()
    {
        try
        {
            while (await _input.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (_input.Reader.TryRead(out var input))
                {
                    Apply(input);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Apply(LiveInput input)
    {
        if (input is LiveInput.Drain drain)
        {
            drain.Completion.TrySetResult();
            return;
        }

        if (input is LiveInput.Event { Value: AgentEvent.ToolFinished finished })
        {
            _terminal.WriteBlock(SpectreBlocks.ToolResult(finished));
        }

        if (_state.Apply(input) && input is LiveInput.Frame)
        {
            _terminal.Render(Snapshot());
        }
    }
}

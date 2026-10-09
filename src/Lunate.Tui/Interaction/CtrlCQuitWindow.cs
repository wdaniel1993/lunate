using System.Reactive.Concurrency;

namespace Lunate.Tui;

internal enum CtrlCAction
{
    ClearInput,
    Arm,
    Quit,
    ReArm,
}

/// <summary>
/// The Ctrl+C contract: non-empty input clears (and disarms); empty input arms a quit hint and a
/// second press within the window quits. All time comes from the injected scheduler, so tests run
/// on virtual time. There is no cancel path: Ctrl+C never cancels a running turn.
/// </summary>
internal sealed class CtrlCQuitWindow
{
    public const string Hint = "press Ctrl+C again to quit";

    private static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(2);

    private readonly IScheduler _scheduler;
    private readonly TimeSpan _window;
    private bool _armed;
    private DateTimeOffset _armedAt;

    public CtrlCQuitWindow(IScheduler scheduler, TimeSpan? window = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
        _window = window ?? DefaultWindow;
    }

    public bool IsArmed => _armed;

    public CtrlCAction Press(bool inputEmpty)
    {
        if (!inputEmpty)
        {
            _armed = false;
            return CtrlCAction.ClearInput;
        }

        var now = _scheduler.Now;
        if (_armed && now - _armedAt <= _window)
        {
            _armed = false;
            return CtrlCAction.Quit;
        }

        bool wasArmed = _armed;
        _armed = true;
        _armedAt = now;
        return wasArmed ? CtrlCAction.ReArm : CtrlCAction.Arm;
    }
}

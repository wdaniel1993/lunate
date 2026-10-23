using System.Reactive.Concurrency;

namespace Lunate.Tui;

internal enum QuitAction
{
    ClearInput,
    Arm,
    Quit,
    ReArm,
    Ignore,
}

/// <summary>
/// A double-press quit window driven only by the injected scheduler, so tests run on virtual time.
/// Ctrl+C clears a non-empty input and arms on an empty one (two seconds); Ctrl+D arms only when it
/// is eligible — an empty prompt with nothing running — and quits on a second press within 800 ms.
/// </summary>
internal sealed class QuitWindow
{
    public const string Hint = "press Ctrl+C again to quit";
    public const string ExitHint = "press Ctrl+D again to quit";

    private static readonly TimeSpan CtrlCWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExitWindow = TimeSpan.FromMilliseconds(800);

    private readonly IScheduler _scheduler;
    private readonly TimeSpan _window;
    private bool _armed;
    private DateTimeOffset _armedAt;

    private QuitWindow(IScheduler scheduler, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
        _window = window;
    }

    /// <summary>Ctrl+C: clear when there is text, otherwise arm or quit.</summary>
    public static QuitWindow ForCtrlC(IScheduler scheduler) => new(scheduler, CtrlCWindow);

    /// <summary>Ctrl+D: arm or quit when eligible, ignore otherwise.</summary>
    public static QuitWindow ForExit(IScheduler scheduler) => new(scheduler, ExitWindow);

    public bool IsArmed => _armed;

    public QuitAction Press(bool inputEmpty)
    {
        if (!inputEmpty)
        {
            _armed = false;
            return QuitAction.ClearInput;
        }

        return ArmOrQuit();
    }

    public QuitAction PressWhen(bool eligible)
    {
        if (!eligible)
        {
            _armed = false;
            return QuitAction.Ignore;
        }

        return ArmOrQuit();
    }

    private QuitAction ArmOrQuit()
    {
        var now = _scheduler.Now;
        if (_armed && now - _armedAt <= _window)
        {
            _armed = false;
            return QuitAction.Quit;
        }

        bool wasArmed = _armed;
        _armed = true;
        _armedAt = now;
        return wasArmed ? QuitAction.ReArm : QuitAction.Arm;
    }
}

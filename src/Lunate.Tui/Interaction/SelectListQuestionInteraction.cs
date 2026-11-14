using System.Reactive.Concurrency;

namespace Lunate.Tui;

/// <summary>
/// Applies the approval prompt's type-ahead guard to one active question. Starting each question
/// takes a fresh key-sequence and virtual-time stamp; keys read before that stamp or during its
/// first 300 ms are discarded rather than queued.
/// <para>
/// The stamp is readable from another thread (<see cref="ArmedForNextKey"/>) and ageable without
/// moving the clock (<see cref="ExpireArmWindow"/>): a harness that waits for the picker's frame and
/// then presses a key would otherwise sit on the window's edge, where the 300 ms of a virtual clock
/// that a test advanced by hand lands exactly on it and the key is dropped.
/// </para>
/// </summary>
internal sealed class SelectListQuestionInteraction
{
    private static readonly TimeSpan ArmWindow = TimeSpan.FromMilliseconds(300);

    private readonly IScheduler _scheduler;
    private SelectListQuestionModel? _question;

    // Ticks rather than a DateTimeOffset: the harness reads and ages the stamp from its own thread
    // while the key pump reads it, and a 16-byte struct is not read atomically.
    private long _armedAtTicks;
    private long _armSequence;

    public SelectListQuestionInteraction(IScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
    }

    public SelectListQuestionModel Question =>
        _question ?? throw new InvalidOperationException("No question is active.");

    /// <summary>Activates a question and starts its independent arming window.</summary>
    public void BeginQuestion(SelectListQuestionModel question, long lastKeySequence)
    {
        ArgumentNullException.ThrowIfNull(question);
        _question = question;
        _armSequence = lastKeySequence;
        Volatile.Write(ref _armedAtTicks, _scheduler.Now.UtcTicks);
    }

    /// <summary>
    /// Whether a key read at <paramref name="sequence"/> would decide this question (test seam). A
    /// harness that presses a key on the picker needs this: the question must be stamped and past
    /// its in-flight window, not merely visible, before a key counts.
    /// </summary>
    public bool ArmedForNextKey(long sequence)
    {
        long armAgeTicks = _scheduler.Now.UtcTicks - Volatile.Read(ref _armedAtTicks);
        return _question is not null && sequence > _armSequence && armAgeTicks > ArmWindow.Ticks;
    }

    /// <summary>
    /// Ages this question's arm past its in-flight window (test seam). The scheduler is also the live
    /// area's own clock, so a test that advanced it just to get past the arm would age every duration
    /// it then asserts; this says "the window has elapsed" without moving the frame. It is the
    /// approval prompt's seam for the same reason: a virtual clock never closes a window on its own,
    /// and a test whose advance stops exactly at the boundary has its keys dropped.
    /// </summary>
    public void ExpireArmWindow() =>
        Volatile.Write(
            ref _armedAtTicks,
            _scheduler.Now.UtcTicks - ArmWindow.Ticks - TimeSpan.FromMilliseconds(1).Ticks
        );

    /// <summary>Applies an input only after this question is armed.</summary>
    public SelectListQuestionModel Handle(KeyEvent key, long sequence)
    {
        ArgumentNullException.ThrowIfNull(key);
        SelectListQuestionModel current = Question;
        if (!ArmedForNextKey(sequence))
        {
            return current;
        }

        _question = current.Handle(key);
        return _question;
    }
}

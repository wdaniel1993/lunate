using System.Reactive.Concurrency;

namespace Lunate.Tui;

/// <summary>
/// Applies the approval prompt's type-ahead guard to one active question. Starting each question
/// takes a fresh key-sequence and virtual-time stamp; keys read before that stamp or during its
/// first 300 ms are discarded rather than queued.
/// </summary>
internal sealed class SelectListQuestionInteraction
{
    private static readonly TimeSpan ArmWindow = TimeSpan.FromMilliseconds(300);

    private readonly IScheduler _scheduler;
    private SelectListQuestionModel? _question;
    private DateTimeOffset _armedAt;
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
        _armedAt = _scheduler.Now;
    }

    /// <summary>Applies an input only after this question is armed.</summary>
    public SelectListQuestionModel Handle(KeyEvent key, long sequence)
    {
        ArgumentNullException.ThrowIfNull(key);
        SelectListQuestionModel current = Question;
        if (sequence <= _armSequence || _scheduler.Now - _armedAt <= ArmWindow)
        {
            return current;
        }

        _question = current.Handle(key);
        return _question;
    }
}

using System.Runtime.CompilerServices;

namespace Lunate.Tui;

// The live area's one notice slot: the text, its generation, and the rule that a clear never takes a
// notice no frame has shown. A post starts a generation; a paint records the generation it showed; a
// clear that arrives first is held, so the next frame shows the notice once and the frame after that
// drops it. The journal records all three, so a clear that would have eaten an unseen notice reads as
// data instead of a wait timing out with no frame containing the text.
internal sealed partial class LiveArea
{
    /// <summary>The journal's origin for a paint (posts and clears carry their caller).</summary>
    private const string PaintOrigin = "paint";

    private Action<NoticeSlotEntry>? _noticeObserver;
    private int _noticeSequence;
    private int _paintedNoticeGeneration;
    private bool _noticeClearPending;

    /// <summary>
    /// Sets the live area's one notice; null clears it. A clear is honest about the frame: a notice
    /// no paint has shown yet is held for one frame rather than dropped, so a caller that clears
    /// while the input pipeline is still running cannot swallow the notice that same pipeline posts.
    /// <paramref name="originFile"/> and <paramref name="originLine"/> are the caller's, filled in by
    /// the compiler for the journal — they name the clear that would otherwise take a notice silently.
    /// </summary>
    public void SetNotice(
        string? notice,
        [CallerFilePath] string originFile = "",
        [CallerLineNumber] int originLine = 0
    ) =>
        Post(
            new NoticeInput(
                notice,
                notice is null ? 0 : Interlocked.Increment(ref _noticeSequence),
                Origin(originFile, originLine)
            )
        );

    /// <summary>
    /// Attaches the notice slot's journal: every post, clear and paint with the slot generation.
    /// The tests' and stress runs' view of a slot whose whole lifetime is otherwise invisible.
    /// </summary>
    internal void ObserveNotices(Action<NoticeSlotEntry> observe) => _noticeObserver = observe;

    /// <summary>
    /// Applies one notice post or clear to the slot and returns its journal entry (null when a
    /// clear found nothing to clear). Called under <see cref="_postGate"/>.
    /// </summary>
    private NoticeSlotEntry? ApplyNotice(NoticeInput notice)
    {
        if (notice.Notice is null)
        {
            if (_state.Notice is not { } cleared)
            {
                return null;
            }

            int generation = _state.NoticeGeneration;
            bool painted = generation == _paintedNoticeGeneration;
            if (painted)
            {
                _state = _state with { Notice = null, NoticeGeneration = 0 };
            }
            else
            {
                // Held until a frame has shown it: the notice stays, so the next paint cannot miss it.
                _noticeClearPending = true;
            }

            _dirty = true;
            return new NoticeSlotEntry(
                NoticeSlotChange.Cleared,
                cleared,
                generation,
                Deferred: !painted,
                notice.Origin
            );
        }

        // A post always starts a new generation: a re-post of the same text is a fresh notice the
        // user is entitled to see again, and it supersedes a clear held for the previous one.
        _state = _state with
        {
            Notice = notice.Notice,
            NoticeGeneration = notice.Generation,
        };
        _noticeClearPending = false;
        _dirty = true;
        return new NoticeSlotEntry(
            NoticeSlotChange.Posted,
            notice.Notice,
            notice.Generation,
            Deferred: false,
            notice.Origin
        );
    }

    /// <summary>
    /// Records that the painted frame showed the slot's notice, and finishes a clear held for that
    /// generation: the notice has been seen once, so the clear takes effect and the next tick paints
    /// the area without it. A notice posted while the frame was written carries a newer generation
    /// and is never dropped here; a notice that stays up is journaled once.
    /// </summary>
    private void CompleteNoticePaint(LiveAreaState painted)
    {
        if (painted.Notice is not { } notice)
        {
            return;
        }

        bool first;
        lock (_postGate)
        {
            first = painted.NoticeGeneration != _paintedNoticeGeneration;
            _paintedNoticeGeneration = painted.NoticeGeneration;
            if (
                _noticeClearPending
                && _state.Notice is not null
                && _state.NoticeGeneration == painted.NoticeGeneration
            )
            {
                _noticeClearPending = false;
                _state = _state with { Notice = null, NoticeGeneration = 0 };
                _dirty = true;
            }
        }

        if (first)
        {
            _noticeObserver?.Invoke(
                new NoticeSlotEntry(
                    NoticeSlotChange.Painted,
                    notice,
                    painted.NoticeGeneration,
                    Deferred: false,
                    PaintOrigin
                )
            );
        }
    }

    private static string Origin(string file, int line) =>
        FormattableString.Invariant($"{Path.GetFileName(file)}:{line}");
}

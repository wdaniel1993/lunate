using System.Globalization;
using Lunate.Agent;
using Lunate.Tui;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    private readonly FollowUpQueue _followUps = new();

    /// <summary>
    /// Guards the one decision that hands a turn on: enqueue-or-take-the-turn (the queueing
    /// continuation) and drain-or-go-idle (the turn's own flow) are the same step, so the queue and
    /// the turn can never both leave an entry to the other. Held for that decision only — the turn
    /// is opened in the transcript after it, with the turn already claimed.
    /// </summary>
    private readonly object _handoffGate = new();

    /// <summary>
    /// Test seam: awaited on the turn's own flow at its handoff — the run has returned and the
    /// turn's ownership is still undecided, before the queue is read and before the session can go
    /// idle. Production leaves this null; a test suspends the turn here to interleave a follow-up
    /// with that decision, the one window it cannot hold still from outside the session.
    /// </summary>
    internal Func<Task>? TurnHandoffSeam { get; set; }

    /// <summary>How many follow-ups wait behind the running turn (the tests' view).</summary>
    internal int QueuedFollowUpCount => _followUps.Count;

    /// <summary>The queue itself (the tests' view); production reaches it only through the keys.</summary>
    internal FollowUpQueue FollowUps => _followUps;

    /// <summary>The queued follow-ups in submission order (the tests' view).</summary>
    internal IReadOnlyList<QueuedFollowUp> QueuedFollowUps => _followUps.Entries;

    private static string QueueFullNotice =>
        FormattableString.Invariant(
            $"the follow-up queue holds at most {FollowUpQueue.Capacity} messages; take them back with Up or wait for the turn to finish"
        );

    /// <summary>
    /// The send key while a turn runs: prepares the line exactly as a submission would (hooks and
    /// image resolution) and queues the result without starting a run. A refusal leaves the line
    /// untouched so the user can fix it or submit it normally.
    /// </summary>
    private async Task QueueFollowUpAsync(CancellationToken ct)
    {
        string text = _input.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (
            Commands.IsCommand(text)
            && !ImageInputResolver.IsDroppedImagePath(_workspace.WorktreeRoot, text)
        )
        {
            Notify(
                "a slash command cannot be queued; wait for the turn to finish and submit it",
                NoticeLevel.Warning
            );
            return;
        }

        if (_followUps.Count >= FollowUpQueue.Capacity)
        {
            Notify(QueueFullNotice, NoticeLevel.Warning);
            return;
        }

        _live.SetNotice(null);
        InputPipelineResult processed = await _pipeline.ProcessAsync(text, ct);
        if (processed.Consumed)
        {
            Notify("input consumed by an extension hook");
            return;
        }

        ImageInputResult input = await _imageResolver.ResolveAsync(processed.Text, ct);
        if (input.Outcome == ImageInputOutcome.Refused)
        {
            Notify(input.Error!, NoticeLevel.Error);
            return;
        }

        if (input.Outcome == ImageInputOutcome.Quiet)
        {
            return;
        }

        if (
            !_followUps.TryEnqueue(
                new QueuedFollowUp(text, new UserInput(input.Text, input.Images))
            )
        )
        {
            Notify(QueueFullNotice, NoticeLevel.Warning);
            return;
        }

        SetInput(string.Empty);
        UpdateFollowUpIndicator();
        // The turn may have ended while this line was still running its input hooks: its drain found
        // an empty queue and released the turn, so nothing would ever start this entry. Taking the
        // entry and releasing the turn are the two halves of one decision and share the handoff gate,
        // so an entry queued as a turn hands over either starts its own turn here or is drained by the
        // turn that has not released yet.
        QueuedFollowUp? pending = null;
        lock (_handoffGate)
        {
            if (!_turnActive && _followUps.TryDequeue(out pending) && pending is not null)
            {
                // Claimed under the gate, before the transcript opens: the next queueing
                // continuation queues behind this turn instead of starting a second one, and the
                // release that freed the turn has already nulled the handle this assigns below.
                _turnActive = true;
            }
        }

        if (pending is not null)
        {
            UpdateFollowUpIndicator();
            _history.Add(pending.Input.Text);
            PrepareTurn(pending.Input);
            _run = RunTurnsAsync(pending.Input, skill: null, ct);
        }
    }

    /// <summary>
    /// The turn's drain-or-go-idle, as one step under the handoff gate: the head of the queue becomes
    /// the next turn, or the turn is released and the session reads idle. Returns the entry the
    /// running turn takes, or null when it released the turn.
    /// </summary>
    private QueuedFollowUp? TakeNextTurn()
    {
        lock (_handoffGate)
        {
            if (_followUps.TryDequeue(out QueuedFollowUp? next) && next is not null)
            {
                // The turn stays this flow's: it still reads active, so a follow-up queued in the
                // meantime waits for the turn being opened instead of taking it a second time.
                return next;
            }

            ReleaseTurnLocked();
            return null;
        }
    }

    /// <summary>
    /// Releases the turn and its run handle together, so a session that reads idle holds no handle a
    /// handed-off turn could null out from under the new one.
    /// </summary>
    private void ReleaseTurn()
    {
        lock (_handoffGate)
        {
            ReleaseTurnLocked();
        }
    }

    /// <summary>Releases the turn; the caller holds the handoff gate.</summary>
    private void ReleaseTurnLocked()
    {
        _turnActive = false;
        _run = null;
    }

    /// <summary>
    /// Up with a non-empty queue: every entry returns to the editor in order, newline-separated,
    /// ahead of whatever was already typed, and the queue empties. Taking the whole queue back is
    /// the point — the entries read as one block the user meant to edit together.
    /// </summary>
    private void TakeFollowUpsBack()
    {
        IReadOnlyList<string> texts = _followUps.DrainEditorTexts();
        if (texts.Count == 0)
        {
            return;
        }

        string restored = string.Join("\n", texts);
        SetInput(_input.Text.Length == 0 ? restored : restored + "\n" + _input.Text);
        UpdateFollowUpIndicator();
    }

    private void UpdateFollowUpIndicator()
    {
        IReadOnlyList<QueuedFollowUp> queued = _followUps.Entries;
        _live.SetQueued(queued.Count, queued.Count == 0 ? null : queued[0].EditorText);
    }
}

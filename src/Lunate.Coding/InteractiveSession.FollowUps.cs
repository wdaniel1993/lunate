using System.Globalization;
using Lunate.Agent;
using Lunate.Tui;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    private readonly FollowUpQueue _followUps = new();

    /// <summary>Set when Esc cancels a turn; the drain checks it before launching the next message.</summary>
    private volatile bool _turnCancelled;

    /// <summary>How many follow-ups wait behind the running turn (the tests' view).</summary>
    internal int QueuedFollowUpCount => _followUps.Count;

    /// <summary>The queued follow-ups in submission order (the tests' view).</summary>
    internal IReadOnlyList<QueuedFollowUp> QueuedFollowUps => _followUps.Entries;

    private static string QueueFullNotice =>
        FormattableString.Invariant(
            $"the follow-up queue holds at most {FollowUpQueue.Capacity} messages; take one back with Alt+Up or wait for the turn to finish"
        );

    /// <summary>
    /// Alt+Enter during a turn: prepares the line exactly as a submission would (hooks and image
    /// resolution) and queues the result without starting or steering a run. A refusal leaves the
    /// line untouched so the user can fix it or submit it normally.
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
    }

    private void UpdateFollowUpIndicator() => _live.SetQueuedCount(_followUps.Count);
}

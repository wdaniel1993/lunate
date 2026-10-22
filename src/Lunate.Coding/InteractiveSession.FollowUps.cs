namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    private readonly FollowUpQueue _followUps = new();

    /// <summary>How many follow-ups wait behind the running turn (the tests' view).</summary>
    internal int QueuedFollowUpCount => _followUps.Count;

    /// <summary>The queued follow-ups in submission order (the tests' view).</summary>
    internal IReadOnlyList<QueuedFollowUp> QueuedFollowUps => _followUps.Entries;

    private void UpdateFollowUpIndicator() => _live.SetQueuedCount(_followUps.Count);
}

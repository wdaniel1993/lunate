using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// One queued follow-up: the prepared input a run will submit and the line the editor gets back
/// when the user takes the entry out of the queue.
/// </summary>
internal sealed record QueuedFollowUp(string EditorText, UserInput Input);

/// <summary>
/// The bounded in-memory follow-up queue: messages run from the head in submission order, while
/// taking back drains the whole queue in that same order. Nothing here is persisted.
/// </summary>
internal sealed class FollowUpQueue
{
    internal const int Capacity = 5;

    private readonly object _gate = new();
    private readonly List<QueuedFollowUp> _entries = [];

    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    internal IReadOnlyList<QueuedFollowUp> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    internal bool TryEnqueue(QueuedFollowUp entry)
    {
        lock (_gate)
        {
            if (_entries.Count >= Capacity)
            {
                return false;
            }

            _entries.Add(entry);
            return true;
        }
    }

    internal bool TryDequeue(out QueuedFollowUp? entry)
    {
        lock (_gate)
        {
            if (_entries.Count == 0)
            {
                entry = null;
                return false;
            }

            entry = _entries[0];
            _entries.RemoveAt(0);
            return true;
        }
    }

    internal IReadOnlyList<string> DrainEditorTexts()
    {
        lock (_gate)
        {
            List<string> texts = [.. _entries.Select(entry => entry.EditorText)];
            _entries.Clear();
            return texts;
        }
    }
}

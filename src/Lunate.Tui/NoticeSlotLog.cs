using System.Text;

namespace Lunate.Tui;

/// <summary>One change to the live area's one notice slot.</summary>
internal enum NoticeSlotChange
{
    Posted,
    Cleared,
    Painted,
}

/// <summary>
/// One entry in the notice slot's history: the change, the notice text it carried, the slot
/// generation of that text, and the <c>file:line</c> that made it. <see cref="Deferred"/> marks a
/// clear that arrived before a frame had shown the notice: the slot held it so the next paint
/// still shows it once, instead of discarding a notice the user was meant to see.
/// </summary>
internal readonly record struct NoticeSlotEntry(
    NoticeSlotChange Change,
    string Text,
    int Generation,
    bool Deferred,
    string Origin
)
{
    public override string ToString() =>
        FormattableString.Invariant(
            $"{Change.ToString().ToLowerInvariant()} gen {Generation} \"{Text}\"{(Deferred ? " (deferred)" : string.Empty)} at {Origin}"
        );
}

/// <summary>
/// The notice slot's history, for tests and stress runs: post, clear and paint in the order the
/// live area saw them, with the slot generation. A wait that times out only says "no frame
/// contained the text"; this says which clear took the notice and whether a paint had shown that
/// generation first, which is the difference between a lost notice and a slow one.
/// </summary>
internal sealed class NoticeSlotLog
{
    private readonly object _gate = new();
    private readonly List<NoticeSlotEntry> _entries = [];

    /// <summary>Records one change; the live area calls this for every post, clear and paint.</summary>
    public void Record(NoticeSlotEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>The history in arrival order. A snapshot: the session thread appends.</summary>
    public IReadOnlyList<NoticeSlotEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>The entries of one kind, in arrival order.</summary>
    public IReadOnlyList<NoticeSlotEntry> Changes(NoticeSlotChange change) =>
        [.. Entries.Where(entry => entry.Change == change)];

    /// <summary>The generations a frame has shown, in paint order.</summary>
    public IReadOnlyList<int> PaintedGenerations =>
        [
            .. Entries
                .Where(entry => entry.Change is NoticeSlotChange.Painted)
                .Select(entry => entry.Generation),
        ];

    /// <summary>
    /// The clears that discarded a notice no paint had shown — a notice the user never saw. The
    /// product keeps this empty by holding such a clear until a frame has shown the notice once.
    /// </summary>
    public IReadOnlyList<NoticeSlotEntry> LostNotices
    {
        get
        {
            var painted = new HashSet<int>();
            var lost = new List<NoticeSlotEntry>();
            foreach (NoticeSlotEntry entry in Entries)
            {
                if (entry.Change is NoticeSlotChange.Painted)
                {
                    painted.Add(entry.Generation);
                }
                else if (
                    entry.Change is NoticeSlotChange.Cleared
                    && !entry.Deferred
                    && !painted.Contains(entry.Generation)
                )
                {
                    lost.Add(entry);
                }
            }

            return lost;
        }
    }

    /// <summary>The whole history, one entry per line: the diagnosis a timeout cannot give.</summary>
    public override string ToString()
    {
        IReadOnlyList<NoticeSlotEntry> entries = Entries;
        if (entries.Count == 0)
        {
            return "notice slot: empty";
        }

        var text = new StringBuilder("notice slot:");
        foreach (NoticeSlotEntry entry in entries)
        {
            text.AppendLine().Append("  ").Append(entry.ToString());
        }

        return text.ToString();
    }
}

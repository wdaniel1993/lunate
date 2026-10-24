namespace Lunate.Agent;

/// <summary>
/// The one place a session's shape is read: which entry is the active leaf and which entries lie on
/// that leaf's <c>parentId</c> chain. Every reader — the request builder, <c>/tree</c>, <c>/fork</c>
/// and the compaction lookup — asks here, so the rule below has a single implementation.
/// </summary>
/// <remarks>
/// The rule, verbatim from the spec: the active leaf is the most recently appended entry, unless a
/// later active-leaf entry points elsewhere. A file with no such entry therefore resolves exactly
/// as it did before the tree kinds existed.
/// </remarks>
internal static class SessionTree
{
    /// <summary>The active leaf's entry id, or null for a session with no entries.</summary>
    internal static string? ActiveLeaf(IReadOnlyList<SessionEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string? leaf = null;
        foreach (SessionEntry entry in entries)
        {
            leaf = entry is SessionActiveLeafEntry move ? move.LeafId : entry.Id;
        }

        return leaf;
    }

    /// <summary>
    /// The entries on <paramref name="leaf"/>'s chain, root first, leaf last; empty when the leaf
    /// is null or unknown. A move entry is a marker, never a path entry, and a cycle (a corrupted
    /// file) stops the walk instead of looping.
    /// </summary>
    internal static IReadOnlyList<SessionEntry> Path(
        IReadOnlyList<SessionEntry> entries,
        string? leaf
    )
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (leaf is null)
        {
            return [];
        }

        Dictionary<string, SessionEntry> byId = new(entries.Count, StringComparer.Ordinal);
        foreach (SessionEntry entry in entries)
        {
            byId[entry.Id] = entry;
        }

        List<SessionEntry> chain = [];
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? current = leaf;
        while (
            current is not null && visited.Add(current) && byId.TryGetValue(current, out var entry)
        )
        {
            chain.Add(entry);
            current = ParentOf(entry);
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// The session's whole tree as rows, in file order: every content entry with its depth from the
    /// root and whether it lies on the active path. A move entry is a marker and never a row; a
    /// missing parent makes an entry a root, and a cycle stops that entry's walk instead of looping.
    /// </summary>
    internal static IReadOnlyList<SessionTreeRow> Rows(IReadOnlyList<SessionEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Dictionary<string, SessionEntry> byId = new(entries.Count, StringComparer.Ordinal);
        foreach (SessionEntry entry in entries)
        {
            byId[entry.Id] = entry;
        }

        HashSet<string> onActivePath = new(
            Path(entries, ActiveLeaf(entries)).Select(static entry => entry.Id),
            StringComparer.Ordinal
        );

        List<SessionTreeRow> rows = [];
        foreach (SessionEntry entry in entries)
        {
            if (entry is SessionActiveLeafEntry)
            {
                continue;
            }

            rows.Add(
                new SessionTreeRow(entry, Depth(entry, byId), onActivePath.Contains(entry.Id))
            );
        }

        return rows;
    }

    /// <summary>
    /// How far <paramref name="entry"/> sits below its root, counted along its own parent chain: a
    /// missing parent ends the walk (the entry is a root) and a cycle, or a marker standing where a
    /// parent should be, stops it — the same discipline as <see cref="Path"/>, so a corrupted file
    /// renders instead of throwing.
    /// </summary>
    private static int Depth(SessionEntry entry, Dictionary<string, SessionEntry> byId)
    {
        int depth = 0;
        HashSet<string> visited = new(StringComparer.Ordinal) { entry.Id };
        string? current = ParentOf(entry);
        while (
            current is not null
            && visited.Add(current)
            && byId.TryGetValue(current, out SessionEntry? parent)
            && parent is not SessionActiveLeafEntry
        )
        {
            depth++;
            current = ParentOf(parent);
        }

        return depth;
    }

    /// <summary>
    /// The last compaction on <paramref name="path"/>: a compaction applies only when its own entry
    /// is on the chain, so a compaction written on a branch that is no longer active is dead
    /// weight rather than every branch's summary.
    /// </summary>
    internal static SessionCompactionEntry? CompactionOn(IReadOnlyList<SessionEntry> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        for (int index = path.Count - 1; index >= 0; index--)
        {
            if (path[index] is SessionCompactionEntry compaction)
            {
                return compaction;
            }
        }

        return null;
    }

    /// <summary>The entry's parent id, or null for an entry kind that carries no chain.</summary>
    private static string? ParentOf(SessionEntry entry) =>
        entry switch
        {
            SessionMessageEntry message => message.ParentId,
            SessionCompactionEntry compaction => compaction.ParentId,
            SessionModelChangeEntry modelChange => modelChange.ParentId,
            SessionActiveToolsEntry activeTools => activeTools.ParentId,
            SessionPromptSectionEntry promptSection => promptSection.ParentId,
            SessionChildSessionEntry childSession => childSession.ParentId,
            SessionNestedCallsEntry nestedCalls => nestedCalls.ParentId,
            SessionExtensionEntry extension => extension.ParentId,
            SessionBranchSummaryEntry branchSummary => branchSummary.ParentId,
            _ => null,
        };
}

/// <summary>
/// One row of a session's tree: a content entry, its depth from the root (0) and whether it lies on
/// the active path. The row carries the entry itself so a reader can render it without asking the
/// file again.
/// </summary>
internal readonly record struct SessionTreeRow(SessionEntry Entry, int Depth, bool OnActivePath);

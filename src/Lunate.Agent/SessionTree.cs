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

namespace Lunate.Agent;

public sealed partial class Session
{
    /// <summary>The entries on the active leaf's chain, root to leaf: exactly what the model sees.</summary>
    internal IReadOnlyList<SessionEntry> ActivePath => SessionTree.Path(_entries, ActiveLeafId);

    /// <summary>
    /// Moves the active leaf, appending one <see cref="SessionActiveLeafEntry"/> that names it. The
    /// file is never rewritten: the branch that is left stays in it, recoverable, and the next
    /// append hangs off the new leaf. A leaf the file does not contain is refused, and a move entry
    /// is never a leaf (it is a marker, not a path entry).
    /// </summary>
    internal void MoveLeaf(string leafId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leafId);
        SessionEntry target =
            _entries.FirstOrDefault(entry =>
                string.Equals(entry.Id, leafId, StringComparison.Ordinal)
            )
            ?? throw new ArgumentException(
                $"Session '{SessionId}' has no entry '{leafId}'; the leaf must name an entry this session contains.",
                nameof(leafId)
            );
        if (target is SessionActiveLeafEntry)
        {
            throw new ArgumentException(
                $"Session '{SessionId}' entry '{leafId}' moves the leaf rather than carrying content; pick the entry the leaf should point at.",
                nameof(leafId)
            );
        }

        string? from = ActiveLeafId;
        if (string.Equals(from, leafId, StringComparison.Ordinal))
        {
            return;
        }

        AddEntry(
            new SessionActiveLeafEntry(NextEntryId(), from, _timeProvider.GetUtcNow(), leafId)
        );
    }

    /// <summary>
    /// Writes the summary of the branch that was left as the first entry after the leaf moved to:
    /// its parent is the new leaf, so it is ordinary context on the destination path and invisible
    /// from the branch it describes. It carries no <c>replaces</c> list and is not a compaction.
    /// </summary>
    internal void AppendBranchSummary(string fromLeafId, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromLeafId);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (ActiveLeafId is not { } leaf)
        {
            throw new InvalidOperationException(
                $"Session '{SessionId}' has no entries; there is no branch to summarise."
            );
        }

        if (!_entries.Any(entry => string.Equals(entry.Id, fromLeafId, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"Session '{SessionId}' has no entry '{fromLeafId}'; the abandoned tip must be an entry this session contains.",
                nameof(fromLeafId)
            );
        }

        AddEntry(
            new SessionBranchSummaryEntry(
                NextEntryId(),
                leaf,
                _timeProvider.GetUtcNow(),
                fromLeafId,
                summary
            )
        );
    }

    /// <summary>
    /// Creates a new session file holding the picked entry's own chain, root to that entry, its
    /// header carrying <c>origin</c> and <c>branched</c>. The entry may sit on any branch: a fork
    /// from an abandoned one takes that branch's ancestry, never the active path's. Entry ids and
    /// bytes are copied verbatim, so the fork's prefix is the source's prefix; the follow-up queue
    /// lives in memory and is never copied.
    /// </summary>
    internal Session Fork(string directory, string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        SessionEntry target =
            _entries.FirstOrDefault(entry =>
                string.Equals(entry.Id, entryId, StringComparison.Ordinal)
            )
            ?? throw new ArgumentException(
                $"Session '{SessionId}' has no entry '{entryId}'; /fork takes an entry the session file contains.",
                nameof(entryId)
            );
        if (target is SessionActiveLeafEntry)
        {
            throw new ArgumentException(
                $"Session '{SessionId}' entry '{entryId}' moves the leaf rather than carrying content; /fork takes the entry to branch at.",
                nameof(entryId)
            );
        }

        return WriteBranchFile(directory, entryId, SessionTree.Path(_entries, entryId));
    }

    /// <summary>
    /// Creates a new session file holding every entry of this session, in file order, with the same
    /// <c>origin</c> header shape naming this session and its active leaf.
    /// </summary>
    internal Session Clone(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (ActiveLeafId is not { } leaf)
        {
            throw new InvalidOperationException(
                $"Session '{SessionId}' has no entries; there is nothing to clone."
            );
        }

        return WriteBranchFile(directory, leaf, _entries);
    }

    /// <summary>
    /// Writes a branch file: a fresh header (the current schema, this session's cwd, repo and worktree, the
    /// new id) carrying <c>origin</c> and <c>branched</c>, then the chosen entries verbatim, then
    /// loads it back so the returned session is the file's own truth.
    /// </summary>
    private Session WriteBranchFile(
        string directory,
        string originEntryId,
        IEnumerable<SessionEntry> entries
    )
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string sessionId = NewSessionId(now);
        string path = System.IO.Path.Combine(directory, SessionPaths.SessionFileName(sessionId));
        Directory.CreateDirectory(directory);

        var header = new SessionHeaderEntry(
            sessionId,
            SessionFormat.SchemaVersion,
            Header.Cwd,
            now,
            SessionFormat.MeaiVersion,
            Header.Repo,
            Header.Worktree
        )
        {
            Origin = new SessionOrigin(SessionId, originEntryId),
            Branched = true,
        };

        List<string> lines = [SessionFormat.Serialize(header)];
        lines.AddRange(entries.Select(SessionFormat.Serialize));
        SessionFiles.WriteText(path, string.Join('\n', lines) + "\n");
        return Load(path, _timeProvider);
    }
}

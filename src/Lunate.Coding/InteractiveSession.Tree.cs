using Lunate.Agent;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>The model the tree picker is showing; Enter reads the picked row from it.</summary>
    private TranscriptModel? _treeModel;

    /// <summary>
    /// <c>/tree</c>: the session's whole tree rendered through the transcript viewer in pick mode —
    /// every branch, indented by depth, the active path marked — rather than only the chain the
    /// model sees. Read-only, so it stays available while a run is active; Enter puts the picked
    /// user message back in the editor and sends nothing.
    /// </summary>
    private void OpenTree()
    {
        if (_session is not { } session)
        {
            Notify("no session file to browse");
            return;
        }

        List<TranscriptEntry> entries = [];
        foreach (SessionTreeRow row in SessionTree.Rows(session.Entries))
        {
            switch (row.Entry)
            {
                case SessionMessageEntry { Message.Role: var role } message
                    when Text(message.Message) is { Length: > 0 } text:
                    entries.Add(
                        new TranscriptTreeRowEntry(
                            row.Entry.Id,
                            text,
                            row.Depth,
                            row.OnActivePath,
                            IsUserMessage: role == ChatRole.User
                        )
                    );
                    break;
                case SessionCompactionEntry compaction:
                    entries.Add(
                        new TranscriptTreeRowEntry(
                            row.Entry.Id,
                            compaction.Summary,
                            row.Depth,
                            row.OnActivePath,
                            IsUserMessage: false
                        )
                    );
                    break;
                case SessionBranchSummaryEntry summary:
                    entries.Add(
                        new TranscriptTreeRowEntry(
                            row.Entry.Id,
                            summary.Summary,
                            row.Depth,
                            row.OnActivePath,
                            IsUserMessage: false
                        )
                    );
                    break;
            }
        }

        if (entries.Count == 0)
        {
            Notify("this session has no messages to browse");
            return;
        }

        _treeModel = new TranscriptModel(entries);
        List<int> pickable =
        [
            .. entries
                .Select((entry, index) => (entry, index))
                .Where(pair => pair.entry is TranscriptTreeRowEntry { IsUserMessage: true })
                .Select(pair => pair.index),
        ];
        _live.SetSuspended(true);
        try
        {
            _viewer.Pick(_treeModel, pickable);
        }
        catch
        {
            // The screen never became the picker's: resume the live area before the failure surfaces.
            _live.SetSuspended(false);
            throw;
        }
    }

    /// <summary>One key while the picker owns the terminal; Enter hands the message to the editor.</summary>
    private void HandleTreeKey(KeyEvent key)
    {
        PickOutcome outcome = _viewer.HandlePickKey(key);
        if (outcome is PickOutcome.None)
        {
            return;
        }

        _live.SetSuspended(false);
        if (outcome is not PickOutcome.Picked)
        {
            return;
        }

        if (
            _viewer.PickedEntryIndex is { } picked
            && _treeModel is { } model
            && picked >= 0
            && picked < model.Entries.Count
            && model.Entries[picked] is TranscriptTreeRowEntry { IsUserMessage: true } row
        )
        {
            // Restored, never resent: the message waits in the editor for the person to edit or send.
            SetInput(row.Text);
        }
    }

    /// <summary>
    /// <c>/fork &lt;entry&gt;</c>: writes a new session file holding the picked entry's own chain up
    /// to and including it, its header naming the source session and entry. The current session is
    /// untouched and keeps running on its own file.
    /// </summary>
    private void ForkSession(string argument)
    {
        if (RefusedWhileBusy())
        {
            return;
        }

        if (_session is not { } session)
        {
            Notify("no session file to fork");
            return;
        }

        if (argument.Length == 0)
        {
            Notify("usage: /fork <entry> (pick one with /tree)");
            return;
        }

        try
        {
            Session fork = session.Fork(SessionDirectory(session), argument);
            Notify(
                FormattableString.Invariant(
                    $"forked at {argument} — source: {session.Path} — new: {fork.Path}"
                )
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Notify("could not fork: " + exception.Message, NoticeLevel.Error);
        }
    }

    /// <summary>
    /// <c>/clone</c>: writes a new session file holding every entry of the source, in file order,
    /// with the same origin shape. Dead branches come along because a clone is a copy, not a view.
    /// </summary>
    private void CloneSession()
    {
        if (RefusedWhileBusy())
        {
            return;
        }

        if (_session is not { } session)
        {
            Notify("no session file to clone");
            return;
        }

        try
        {
            Session clone = session.Clone(SessionDirectory(session));
            Notify(
                FormattableString.Invariant(
                    $"cloned {session.SessionId} — source: {session.Path} — new: {clone.Path}"
                )
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
            )
        {
            Notify("could not clone: " + exception.Message, NoticeLevel.Error);
        }
    }

    /// <summary>The directory a fork or clone lands in: the source session file's own.</summary>
    private static string SessionDirectory(Session session) =>
        Path.GetDirectoryName(Path.GetFullPath(session.Path)) ?? ".";

    /// <summary>A message's own text; empty for a message that carries only tool content.</summary>
    private static string Text(ChatMessage message) => message.Text ?? string.Empty;
}

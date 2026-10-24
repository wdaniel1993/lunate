using Lunate.Agent;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding;

/// <summary>
/// One message the copy selector reads: who produced it and what it said. A turn starts at a user
/// message; the selector is only interested in the assistant text inside it.
/// </summary>
internal readonly record struct CopyMessage(bool FromUser, bool FromAssistant, string Text);

/// <summary>
/// Which answer <c>/copy</c> writes (design decision 1): the final assistant message of the most
/// recent completed turn. A turn that ended in an error or a cancellation is a failed tail — its
/// partial answer is not the last answer — so the selector steps back over it to the previous
/// completed turn. Nothing is copied when no turn has completed: an empty clipboard looks like a
/// successful paste, which is worse than a notice.
/// </summary>
internal static class CopyAnswer
{
    /// <summary>
    /// The answer to copy, or null when there is none. <paramref name="lastTurnCompleted"/> is the
    /// session's knowledge of how its most recent run ended; the message list is the record.
    /// </summary>
    internal static string? Last(IReadOnlyList<CopyMessage> messages, bool lastTurnCompleted)
    {
        if (messages.Count == 0)
        {
            return null;
        }

        int end = messages.Count - 1;
        if (!lastTurnCompleted)
        {
            int failedTurnStart = LastUserIndex(messages, end);
            if (failedTurnStart < 0)
            {
                return null;
            }

            end = failedTurnStart - 1;
        }

        int start = LastUserIndex(messages, end) + 1;
        for (var index = end; index >= start; index--)
        {
            if (messages[index] is { FromAssistant: true } message && message.Text.Length > 0)
            {
                return message.Text;
            }
        }

        return null;
    }

    /// <summary>The session's messages, in file order; non-message entries carry no text.</summary>
    internal static IReadOnlyList<CopyMessage> FromEntries(IReadOnlyList<SessionEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        List<CopyMessage> messages = [];
        foreach (SessionEntry entry in entries)
        {
            if (entry is SessionMessageEntry message)
            {
                messages.Add(
                    new CopyMessage(
                        message.Message.Role == ChatRole.User,
                        message.Message.Role == ChatRole.Assistant,
                        message.Message.Text ?? string.Empty
                    )
                );
            }
        }

        return messages;
    }

    /// <summary>
    /// The in-memory transcript of a session that has no file (<c>--no-session</c>): its user and
    /// assistant commits are the same shape without a session entry behind them.
    /// </summary>
    internal static IReadOnlyList<CopyMessage> FromTranscript(
        IReadOnlyList<TranscriptEntry> entries
    )
    {
        ArgumentNullException.ThrowIfNull(entries);
        List<CopyMessage> messages = [];
        foreach (TranscriptEntry entry in entries)
        {
            switch (entry)
            {
                case TranscriptUserEntry user:
                    messages.Add(new CopyMessage(FromUser: true, FromAssistant: false, user.Text));
                    break;
                case TranscriptAssistantEntry assistant:
                    messages.Add(new CopyMessage(false, true, assistant.Markdown));
                    break;
            }
        }

        return messages;
    }

    private static int LastUserIndex(IReadOnlyList<CopyMessage> messages, int from)
    {
        for (var index = from; index >= 0; index--)
        {
            if (messages[index].FromUser)
            {
                return index;
            }
        }

        return -1;
    }
}

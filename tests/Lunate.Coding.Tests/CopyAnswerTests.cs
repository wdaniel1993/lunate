using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// Which answer <c>/copy</c> writes: the final assistant message of the most recent completed turn,
/// with a failed or cancelled tail stepped over, and nothing at all when no turn has completed.
/// </summary>
public sealed class CopyAnswerTests
{
    [Fact]
    public void The_last_answer_of_the_most_recent_completed_turn_is_returned()
    {
        CopyMessage[] messages =
        [
            User("first question"),
            Assistant("first answer"),
            User("second question"),
            Assistant("second answer"),
        ];

        Assert.Equal("second answer", CopyAnswer.Last(messages, lastTurnCompleted: true));
    }

    [Fact]
    public void A_cancelled_tail_is_skipped_and_the_previous_completed_answer_is_returned()
    {
        CopyMessage[] messages =
        [
            User("first question"),
            Assistant("first answer"),
            User("second question"),
            Assistant("half an answ"),
        ];

        Assert.Equal("first answer", CopyAnswer.Last(messages, lastTurnCompleted: false));
    }

    [Fact]
    public void A_failed_tail_with_only_a_tool_call_still_falls_back_to_the_previous_answer()
    {
        CopyMessage[] messages =
        [
            User("first question"),
            Assistant("first answer"),
            User("second question"),
            Assistant(string.Empty),
        ];

        Assert.Equal("first answer", CopyAnswer.Last(messages, lastTurnCompleted: false));
    }

    [Fact]
    public void Nothing_is_returned_when_no_turn_has_completed()
    {
        Assert.Null(CopyAnswer.Last([], lastTurnCompleted: false));
        Assert.Null(CopyAnswer.Last([User("only a question")], lastTurnCompleted: false));
        Assert.Null(CopyAnswer.Last([User("only a question")], lastTurnCompleted: true));
    }

    [Fact]
    public void Only_the_final_assistant_message_of_the_turn_is_returned()
    {
        CopyMessage[] messages =
        [
            User("do it"),
            Assistant("let me look"),
            Assistant(string.Empty),
            Assistant("the final answer"),
        ];

        Assert.Equal("the final answer", CopyAnswer.Last(messages, lastTurnCompleted: true));
    }

    [Fact]
    public void The_session_entries_carry_the_same_answer_with_their_roles()
    {
        SessionMessageEntry[] entries =
        [
            Message("e_01", ChatRole.User, "question"),
            Message("e_02", ChatRole.Assistant, "answer"),
            Message("e_03", ChatRole.User, "follow-up"),
            Message("e_04", ChatRole.Assistant, string.Empty),
        ];

        Assert.Equal(
            "answer",
            CopyAnswer.Last(CopyAnswer.FromEntries(entries), lastTurnCompleted: false)
        );
    }

    private static CopyMessage User(string text) => new(FromUser: true, FromAssistant: false, text);

    private static CopyMessage Assistant(string text) => new(false, true, text);

    private static SessionMessageEntry Message(string id, ChatRole role, string text) =>
        new(id, null, DateTimeOffset.UnixEpoch, new ChatMessage(role, text), null, null);
}

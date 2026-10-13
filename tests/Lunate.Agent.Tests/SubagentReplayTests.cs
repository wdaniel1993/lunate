using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>
/// D13: a parent session with linked child sessions replays deterministically from the committed
/// fixture pair, and each child loads exactly its own history.
/// </summary>
public sealed class SubagentReplayTests
{
    private static string FixturesDirectory =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "fixtures", "sessions");

    private static string ParentPath =>
        Path.Combine(FixturesDirectory, "s_20261013-090000-a1b2.jsonl");

    [Fact]
    public void A_parent_with_a_child_link_replays_deterministically()
    {
        Session first = Session.Load(ParentPath);
        Session second = Session.Load(ParentPath);

        Assert.Equal(
            first.Entries.Select(SessionFormat.Serialize),
            second.Entries.Select(SessionFormat.Serialize)
        );
        Assert.Equal(
            first.ToHistory().Select(message => message.Text),
            second.ToHistory().Select(message => message.Text)
        );

        SessionChildSessionEntry link = Assert.Single(
            first.Entries.OfType<SessionChildSessionEntry>()
        );
        string childPath = Path.Combine(
            FixturesDirectory,
            SessionPaths.SessionFileName(link.ChildSessionId)
        );
        Assert.True(File.Exists(childPath), $"the linked child '{childPath}' should exist");

        Session child = Session.Load(childPath);
        Assert.Equal(link.ChildSessionId, child.SessionId);
        Assert.Equal(
            ["child prompt", "child answer"],
            child.ToHistory().Select(message => message.Text)
        );
        Assert.Equal(
            [ChatRole.User, ChatRole.Assistant],
            child.ToHistory().Select(message => message.Role)
        );
        Assert.Empty(child.Entries.OfType<SessionChildSessionEntry>());
        Assert.Equal(first.Header.Repo, child.Header.Repo);
        Assert.Equal(first.Header.Worktree, child.Header.Worktree);

        // The parent's own history is the parent's: user, spawn call, tool result, final answer.
        List<ChatMessage> parentHistory = first.ToHistory();
        Assert.Equal(4, parentHistory.Count);
        Assert.Equal("Spawn a helper and summarise.", parentHistory[0].Text);
        Assert.Equal(ChatRole.Tool, parentHistory[2].Role);
        Assert.Equal("The helper answered: child answer.", parentHistory[3].Text);
    }
}

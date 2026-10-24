using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>
/// Task 2 of the branching-sessions change: the model sees the active leaf's path, a compaction
/// applies only when its own entry is on that path, and one resolver answers where the leaf is.
/// </summary>
public sealed class SessionTreeContextTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_the_active_path_reaches_the_model()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp, out _);
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("ok"), LoopScripts.Stop());
        AgentHarness harness = Harness(client, session);

        await harness.RunAsync("go", Ct).ToListAsync(Ct);

        Assert.Equal(
            ["user:one", "user:branch-b", "user:go"],
            client
                .Requests[0]
                .Messages.Where(message => message.Role != ChatRole.System)
                .Select(Describe)
        );
    }

    [Fact]
    public async Task A_compaction_on_a_dead_branch_does_not_apply()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp, out _);
        // A compaction written on branch A, before the leaf moved to B.
        session.MoveLeaf("e_02");
        session.AppendCompaction("summary of branch A", ["e_01", "e_02"]);
        session.AppendMessage(new ChatMessage(ChatRole.User, "one more"));
        session.MoveLeaf("e_04");
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("ok"), LoopScripts.Stop());
        AgentHarness harness = Harness(client, session);

        await harness.RunAsync("go", Ct).ToListAsync(Ct);

        Assert.Equal(
            ["user:one", "user:branch-b", "user:go"],
            client
                .Requests[0]
                .Messages.Where(message => message.Role != ChatRole.System)
                .Select(Describe)
        );
    }

    [Fact]
    public async Task A_compaction_on_the_active_path_still_applies_and_off_path_ids_are_ignored()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp, out _);
        // On branch B's path (e_01, e_04), replacing e_01 and an id from the dead branch (e_02).
        session.MoveLeaf("e_04");
        session.AppendCompaction("summary of branch B", ["e_01", "e_02"]);
        session.AppendMessage(new ChatMessage(ChatRole.User, "one more"));
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("ok"), LoopScripts.Stop());
        AgentHarness harness = Harness(client, session);

        await harness.RunAsync("go", Ct).ToListAsync(Ct);

        Assert.Equal(
            ["assistant:summary of branch B", "user:branch-b", "user:one more", "user:go"],
            client
                .Requests[0]
                .Messages.Where(message => message.Role != ChatRole.System)
                .Select(Describe)
        );
    }

    [Fact]
    public void The_leaf_rule_has_one_implementation()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp, out string[] ids);

        Assert.Equal(ids[3], SessionTree.ActiveLeaf(session.Entries));
        Assert.Equal(["e_01", "e_04"], session.ActivePath.Select(entry => entry.Id));
        Assert.Null(SessionTree.CompactionOn(session.ActivePath));

        // A move entry is a marker, never a path entry, and the last move wins.
        Assert.Equal(["e_01", "e_02"], SessionTree.Path(session.Entries, "e_02").Select(e => e.Id));
        Assert.Empty(SessionTree.Path(session.Entries, "e_99"));
    }

    [Fact]
    public void A_session_with_a_cycle_in_its_chain_stops_instead_of_looping()
    {
        List<SessionEntry> entries =
        [
            new SessionMessageEntry(
                "e_01",
                "e_02",
                SessionImageTestData.Start,
                new ChatMessage(ChatRole.User, "a"),
                null,
                null
            ),
            new SessionMessageEntry(
                "e_02",
                "e_01",
                SessionImageTestData.Start,
                new ChatMessage(ChatRole.User, "b"),
                null,
                null
            ),
        ];

        IReadOnlyList<SessionEntry> path = SessionTree.Path(entries, "e_01");

        Assert.Equal(["e_02", "e_01"], path.Select(entry => entry.Id));
    }

    [Fact]
    public void The_request_builder_and_the_viewers_agree_on_the_leaf()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp, out string[] ids);

        Assert.Equal(ids[3], session.ActiveLeafId);
        Assert.Equal(session.ActivePath, SessionTree.Path(session.Entries, session.ActiveLeafId));
    }

    /// <summary>one → two on branch A, one → branch-b on branch B, the leaf on B.</summary>
    private static Session Branched(TempDirectory temp, out string[] ids)
    {
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(SessionImageTestData.Start)
        );
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "two"));
        session.MoveLeaf("e_01");
        session.AppendMessage(new ChatMessage(ChatRole.User, "branch-b"));
        ids = [.. session.Entries.Select(entry => entry.Id)];
        Assert.Equal(["e_01", "e_02", "e_03", "e_04"], ids);
        return session;
    }

    private static string Describe(ChatMessage message) => message.Role.Value + ":" + message.Text;

    private static AgentHarness Harness(ScriptedChatClient client, Session session) =>
        new(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions
            {
                Session = session,
                ModelId = TestCatalog.ModelId,
                ModelCatalog = TestCatalog.WithWindow(128_000),
            }
        );
}

using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>
/// Tasks 3 and 4 of the branching-sessions change at the session seam: a branch summary written
/// where the model can see it, a switch that writes only the leaf entry, and fork/clone provenance.
/// </summary>
public sealed class SessionBranchTests
{
    [Fact]
    public void A_summary_lands_on_the_path_moved_to_as_ordinary_context()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp);

        session.MoveLeaf("e_04");
        session.AppendBranchSummary("e_02", "Branch A introduced the parser.");

        SessionBranchSummaryEntry summary = Assert.Single(
            session.Entries.OfType<SessionBranchSummaryEntry>()
        );
        Assert.Equal("e_04", summary.ParentId);
        Assert.Equal("e_02", summary.FromLeafId);
        Assert.Equal("Branch A introduced the parser.", summary.Summary);

        // The first entry after the leaf moved to B, ordinary context on B's path, absent from A's.
        Assert.Equal(["e_01", "e_04", summary.Id], session.ActivePath.Select(entry => entry.Id));
        Assert.DoesNotContain(
            summary.Id,
            SessionTree.Path(session.Entries, "e_02").Select(entry => entry.Id)
        );
        Assert.Equal(
            ["user:one", "user:branch-b", "assistant:Branch A introduced the parser."],
            session.ToHistory().Select(message => message.Role.Value + ":" + message.Text)
        );
        Assert.Null(SessionTree.CompactionOn(session.ActivePath));

        Session loaded = Session.Load(session.Path);
        Assert.Equal(
            session.Entries.Select(SessionFormat.Serialize),
            loaded.Entries.Select(SessionFormat.Serialize)
        );
        Assert.Equal(
            ["user:one", "user:branch-b", "assistant:Branch A introduced the parser."],
            loaded.ToHistory().Select(message => message.Role.Value + ":" + message.Text)
        );
    }

    [Fact]
    public void A_switch_without_the_option_appends_only_the_active_leaf_entry()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp);
        int before = File.ReadAllLines(session.Path).Length;

        session.MoveLeaf("e_01");

        string[] after = File.ReadAllLines(session.Path);
        Assert.Equal(before + 1, after.Length);
        SessionActiveLeafEntry move = Assert.IsType<SessionActiveLeafEntry>(
            SessionFormat.Parse(after[^1])
        );
        Assert.Equal("e_01", move.LeafId);
        Assert.Equal("e_04", move.ParentId);
        Assert.Empty(session.Entries.OfType<SessionBranchSummaryEntry>());
    }

    [Fact]
    public void Moving_the_leaf_backwards_does_not_rewrite_the_file()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp);
        string[] before = File.ReadAllLines(session.Path);

        session.MoveLeaf("e_01");

        string[] after = File.ReadAllLines(session.Path);
        Assert.Equal(before, after[..^1]);
    }

    [Fact]
    public void A_move_to_an_unknown_entry_is_refused()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            session.MoveLeaf("e_99")
        );
        Assert.Contains("e_99", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fork_starts_at_the_picked_entry_and_records_its_origin()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp);
        string directory = temp.File("forks");
        Directory.CreateDirectory(directory);

        Session fork = session.Fork(directory, "e_01");

        Assert.NotEqual(session.SessionId, fork.SessionId);
        Assert.Equal(4, fork.Schema);
        Assert.Equal(new SessionOrigin(session.SessionId, "e_01"), fork.Header.Origin);
        Assert.True(fork.Header.Branched);
        Assert.Equal(session.Header.Cwd, fork.Header.Cwd);
        Assert.Equal(["e_01"], fork.Entries.Select(entry => entry.Id));
        Assert.Equal("one", Assert.Single(fork.ToHistory()).Text);
        Assert.Equal(Path.Combine(directory, Path.GetFileName(fork.Path)), fork.Path);
    }

    [Fact]
    public void A_fork_of_a_longer_path_copies_the_prefix_verbatim()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp);
        Directory.CreateDirectory(temp.File("forks"));

        Session fork = session.Fork(temp.File("forks"), "e_04");

        Assert.Equal(["e_01", "e_04"], fork.Entries.Select(entry => entry.Id));
        Assert.Equal(
            ["user:one", "user:branch-b"],
            fork.ToHistory().Select(message => message.Role.Value + ":" + message.Text)
        );
        Assert.Equal("e_04", fork.ActiveLeafId);
    }

    [Fact]
    public void A_fork_at_an_entry_off_the_active_path_copies_that_entrys_chain()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp);
        Directory.CreateDirectory(temp.File("forks"));

        // e_02 is the abandoned tip: the fork holds its own chain, not the active path's.
        Session fork = session.Fork(temp.File("forks"), "e_02");

        Assert.Equal(["e_01", "e_02"], fork.Entries.Select(entry => entry.Id));
        Assert.Equal(new SessionOrigin(session.SessionId, "e_02"), fork.Header.Origin);
        Assert.Equal("e_02", fork.ActiveLeafId);
        Assert.Equal(
            ["user:one", "assistant:two"],
            fork.ToHistory().Select(message => message.Role.Value + ":" + message.Text)
        );
        Assert.Equal(["e_01", "e_04"], session.ActivePath.Select(entry => entry.Id));
    }

    [Fact]
    public void A_fork_of_an_entry_the_file_does_not_contain_is_refused()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp);
        Directory.CreateDirectory(temp.File("forks"));

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            session.Fork(temp.File("forks"), "e_99")
        );

        Assert.Contains("e_99", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(temp.File("forks")));
    }

    [Fact]
    public void A_fork_at_the_leaf_move_marker_is_refused()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp);
        Directory.CreateDirectory(temp.File("forks"));

        // e_03 moves the leaf: a marker, never a row and never a fork point.
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            session.Fork(temp.File("forks"), "e_03")
        );

        Assert.Contains("e_03", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(temp.File("forks")));
    }

    [Fact]
    public void A_clone_copies_every_entry_of_the_source_including_dead_branches()
    {
        using var temp = new TempDirectory();
        Session session = Branched(temp);
        Directory.CreateDirectory(temp.File("clones"));

        Session clone = session.Clone(temp.File("clones"));

        Assert.NotEqual(session.SessionId, clone.SessionId);
        Assert.Equal(new SessionOrigin(session.SessionId, "e_04"), clone.Header.Origin);
        Assert.True(clone.Header.Branched);
        Assert.Equal(
            session.Entries.Select(SessionFormat.Serialize),
            clone.Entries.Select(SessionFormat.Serialize)
        );
        Assert.Equal(session.ActiveLeafId, clone.ActiveLeafId);
        Assert.Equal(session.ToHistory().Count, clone.ToHistory().Count);
        Assert.True(File.Exists(clone.Path));
        Assert.NotEqual(session.Path, clone.Path);
    }

    /// <summary>one → two on branch A, one → branch-b on branch B, the leaf on B.</summary>
    private static Session Branched(TempDirectory temp)
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
        return session;
    }
}

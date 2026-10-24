using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>
/// The whole-tree reader behind <c>/tree</c>: every content entry in file order with its depth from
/// the root and whether it lies on the active path. Both branches, so the one the model no longer
/// sees stays visible — and forkable — instead of vanishing from the tree that exists to show it.
/// </summary>
public sealed class SessionTreeRowsTests
{
    [Fact]
    public void Both_branches_appear_in_file_order_with_their_depth_and_path_marker()
    {
        IReadOnlyList<SessionTreeRow> rows = SessionTree.Rows(Branched());

        // The move entry is a marker, never a row; the dead branch keeps its depth and loses the mark.
        Assert.Equal(["e_01", "e_02", "e_04"], rows.Select(row => row.Entry.Id));
        Assert.Equal([0, 1, 1], rows.Select(row => row.Depth));
        Assert.Equal([true, false, true], rows.Select(row => row.OnActivePath));
    }

    [Fact]
    public void An_off_path_chain_keeps_its_own_depth_below_the_active_leaf()
    {
        IReadOnlyList<SessionTreeRow> rows = SessionTree.Rows(LongBranch());

        Assert.Equal(["e_01", "e_02", "e_03", "e_04", "e_06"], rows.Select(row => row.Entry.Id));
        Assert.Equal([0, 1, 2, 3, 1], rows.Select(row => row.Depth));
        Assert.Equal([true, false, false, false, true], rows.Select(row => row.OnActivePath));
    }

    [Fact]
    public void An_entry_whose_parent_is_missing_is_a_root_instead_of_an_error()
    {
        List<SessionEntry> entries =
        [
            Message("e_01", null, ChatRole.User, "one"),
            Message("e_02", "e_99", ChatRole.User, "orphan"),
        ];

        IReadOnlyList<SessionTreeRow> rows = SessionTree.Rows(entries);

        Assert.Equal([0, 0], rows.Select(row => row.Depth));
        Assert.Equal([false, true], rows.Select(row => row.OnActivePath));
    }

    [Fact]
    public void A_cycle_in_the_chain_does_not_throw_and_does_not_loop()
    {
        List<SessionEntry> entries =
        [
            Message("e_01", "e_02", ChatRole.User, "a"),
            Message("e_02", "e_01", ChatRole.User, "b"),
        ];

        IReadOnlyList<SessionTreeRow> rows = SessionTree.Rows(entries);

        Assert.Equal(["e_01", "e_02"], rows.Select(row => row.Entry.Id));
        Assert.All(rows, row => Assert.InRange(row.Depth, 0, entries.Count));
    }

    [Fact]
    public void A_session_with_no_entries_has_no_rows()
    {
        Assert.Empty(SessionTree.Rows([]));
    }

    /// <summary>one → two on branch A, the leaf moved back, one → branch-b on branch B.</summary>
    private static List<SessionEntry> Branched() =>
        [
            Message("e_01", null, ChatRole.User, "one"),
            Message("e_02", "e_01", ChatRole.Assistant, "two"),
            new SessionActiveLeafEntry("e_03", "e_02", SessionImageTestData.Start, "e_01"),
            Message("e_04", "e_01", ChatRole.User, "branch-b"),
        ];

    /// <summary>A four-deep abandoned branch, the leaf moved back to e_01, then one more message.</summary>
    private static List<SessionEntry> LongBranch() =>
        [
            Message("e_01", null, ChatRole.User, "one"),
            Message("e_02", "e_01", ChatRole.Assistant, "two"),
            Message("e_03", "e_02", ChatRole.User, "three"),
            Message("e_04", "e_03", ChatRole.Assistant, "four"),
            new SessionActiveLeafEntry("e_05", "e_04", SessionImageTestData.Start, "e_01"),
            Message("e_06", "e_01", ChatRole.User, "six"),
        ];

    private static SessionMessageEntry Message(
        string id,
        string? parentId,
        ChatRole role,
        string text
    ) => new(id, parentId, SessionImageTestData.Start, new ChatMessage(role, text), null, null);
}

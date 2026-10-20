using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class SessionListTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Summaries_with_the_same_core_fields_are_equal_regardless_of_title()
    {
        var first = new SessionSummary("s_1", "/work/s_1.jsonl", Start, Start.AddMinutes(1))
        {
            Title = "Refactor the parser",
        };
        var second = new SessionSummary("s_1", "/work/s_1.jsonl", Start, Start.AddMinutes(1))
        {
            Title = "Fix the session picker",
        };

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Changing_a_core_field_makes_summaries_unequal()
    {
        var summary = new SessionSummary("s_1", "/work/s_1.jsonl", Start, Start.AddMinutes(1))
        {
            Title = "same title",
        };

        Assert.NotEqual(summary, summary with { Id = "s_2" });
        Assert.NotEqual(summary, summary with { Path = "/work/other.jsonl" });
        Assert.NotEqual(summary, summary with { Created = Start.AddMinutes(1) });
        Assert.NotEqual(summary, summary with { Modified = Start.AddMinutes(2) });
    }

    [Fact]
    public void An_empty_or_missing_directory_lists_nothing()
    {
        using var temp = new TempDirectory();

        Assert.Empty(Session.List(temp.File("missing")));
        Assert.Empty(Session.List(temp.Root));
    }

    [Fact]
    public void Sessions_list_newest_modified_first_with_their_header_metadata()
    {
        using var temp = new TempDirectory();
        Session older = Session.Create(
            temp.File("older.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        Session newer = Session.Create(
            temp.File("newer.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        File.SetLastWriteTimeUtc(temp.File("older.jsonl"), Start.UtcDateTime);
        File.SetLastWriteTimeUtc(temp.File("newer.jsonl"), Start.UtcDateTime.AddMinutes(5));

        IReadOnlyList<SessionSummary> summaries = Session.List(temp.Root);

        Assert.Equal([newer.SessionId, older.SessionId], summaries.Select(summary => summary.Id));
        Assert.Equal(temp.File("newer.jsonl"), summaries[0].Path);
        Assert.Equal(
            new DateTimeOffset(Start.UtcDateTime.AddMinutes(5), TimeSpan.Zero),
            summaries[0].Modified
        );
        Assert.Equal(Start, summaries[0].Created);
    }

    [Fact]
    public void Equal_modification_times_tie_break_on_the_id_descending()
    {
        using var temp = new TempDirectory();
        Session first = Session.Create(temp.File("a.jsonl"), "/work", new FixedTimeProvider(Start));
        Session second = Session.Create(
            temp.File("b.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        File.SetLastWriteTimeUtc(temp.File("a.jsonl"), Start.UtcDateTime);
        File.SetLastWriteTimeUtc(temp.File("b.jsonl"), Start.UtcDateTime);

        IReadOnlyList<SessionSummary> summaries = Session.List(temp.Root);

        Assert.Equal(
            summaries.Select(summary => summary.Id).OrderDescending(StringComparer.Ordinal),
            summaries.Select(summary => summary.Id)
        );
        Assert.Equal(2, summaries.Count);
        Assert.Contains(first.SessionId, summaries.Select(summary => summary.Id));
        Assert.Contains(second.SessionId, summaries.Select(summary => summary.Id));
    }

    [Fact]
    public void Equal_modification_times_tie_break_on_the_created_time_descending()
    {
        using var temp = new TempDirectory();
        Session older = Session.Create(
            temp.File("older.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        Session newer = Session.Create(
            temp.File("newer.jsonl"),
            "/work",
            new FixedTimeProvider(Start.AddMinutes(5))
        );
        File.SetLastWriteTimeUtc(temp.File("older.jsonl"), Start.UtcDateTime);
        File.SetLastWriteTimeUtc(temp.File("newer.jsonl"), Start.UtcDateTime);

        IReadOnlyList<SessionSummary> summaries = Session.List(temp.Root);

        Assert.Equal([newer.SessionId, older.SessionId], summaries.Select(summary => summary.Id));
    }

    [Fact]
    public void The_listing_is_bounded_to_the_twenty_most_recent()
    {
        using var temp = new TempDirectory();
        List<string> ids = [];
        for (var index = 0; index < 25; index++)
        {
            Session session = Session.Create(
                temp.File($"s{index:D2}.jsonl"),
                "/work",
                new FixedTimeProvider(Start.AddSeconds(index))
            );
            ids.Add(session.SessionId);
            File.SetLastWriteTimeUtc(
                temp.File($"s{index:D2}.jsonl"),
                Start.UtcDateTime.AddMinutes(index)
            );
        }

        IReadOnlyList<SessionSummary> summaries = Session.List(temp.Root);

        Assert.Equal(20, summaries.Count);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ids.Skip(5).Reverse(), summaries.Select(summary => summary.Id));
    }

    [Fact]
    public void The_listing_reads_titles_for_the_rows_it_returns()
    {
        using var temp = new TempDirectory();
        List<string> ids = [];
        List<string> titles = [];
        for (var index = 0; index < 25; index++)
        {
            Session session = Session.Create(
                temp.File($"s{index:D2}.jsonl"),
                "/work",
                new FixedTimeProvider(Start.AddSeconds(index))
            );
            ids.Add(session.SessionId);
            titles.Add($"Session {index:D2}");
            SessionTitle.Write(session.Path, titles[^1]);
            File.SetLastWriteTimeUtc(
                temp.File($"s{index:D2}.jsonl"),
                Start.UtcDateTime.AddMinutes(index)
            );
        }

        IReadOnlyList<SessionSummary> summaries = Session.List(temp.Root);

        Assert.Equal(20, summaries.Count);
        Assert.Equal(ids.Skip(5).Reverse(), summaries.Select(summary => summary.Id));
        Assert.Equal(titles.Skip(5).Reverse(), summaries.Select(summary => summary.Title));
    }

    [Fact]
    public void A_corrupt_header_is_skipped_and_the_rest_still_list()
    {
        using var temp = new TempDirectory();
        Session good = Session.Create(
            temp.File("good.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        File.WriteAllText(temp.File("corrupt.jsonl"), "not json at all\n");
        File.WriteAllText(temp.File("half.jsonl"), "{}\n");
        File.SetLastWriteTimeUtc(temp.File("corrupt.jsonl"), Start.UtcDateTime.AddMinutes(9));

        IReadOnlyList<SessionSummary> summaries = Session.List(temp.Root);

        SessionSummary summary = Assert.Single(summaries);
        Assert.Equal(good.SessionId, summary.Id);
    }

    [Fact]
    public void Only_the_header_line_is_needed()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));
        File.AppendAllText(temp.File("s.jsonl"), "this tail line is corrupt\n");

        SessionSummary summary = Assert.Single(Session.List(temp.Root));

        Assert.Equal(session.SessionId, summary.Id);
    }

    [Fact]
    public void Non_jsonl_files_are_ignored()
    {
        using var temp = new TempDirectory();
        Session.Create(temp.File("s.jsonl"), "/work", new FixedTimeProvider(Start));
        File.WriteAllText(temp.File("notes.txt"), "keep me out");

        Assert.Single(Session.List(temp.Root));
    }

    [Fact]
    public void Listing_never_modifies_the_files()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));
        string path = temp.File("s.jsonl");
        byte[] before = File.ReadAllBytes(path);
        DateTime beforeWrite = File.GetLastWriteTimeUtc(path);

        _ = Session.List(temp.Root);

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_titled_session_lists_with_its_title()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        SessionTitle.Write(session.Path, "Refactor the parser");

        SessionSummary summary = Assert.Single(Session.List(temp.Root));

        Assert.Equal("Refactor the parser", summary.Title);
    }

    [Fact]
    public void A_missing_sidecar_lists_no_title()
    {
        using var temp = new TempDirectory();
        Session.Create(temp.File("s.jsonl"), "/work", new FixedTimeProvider(Start));

        SessionSummary summary = Assert.Single(Session.List(temp.Root));

        Assert.Null(summary.Title);
    }

    [Fact]
    public void An_unusable_sidecar_lists_no_title()
    {
        using var temp = new TempDirectory();
        Session blank = Session.Create(
            temp.File("blank.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        Session broken = Session.Create(
            temp.File("broken.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        File.WriteAllText(SessionTitle.PathFor(blank.Path), "\n");
        File.WriteAllBytes(SessionTitle.PathFor(broken.Path), [0xC3, 0x28]);

        IReadOnlyList<SessionSummary> summaries = Session.List(temp.Root);

        Assert.Equal(2, summaries.Count);
        Assert.All(summaries, summary => Assert.Null(summary.Title));
    }
}

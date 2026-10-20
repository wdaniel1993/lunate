using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class SessionSelectionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private const string Cwd = "/work";

    [Fact]
    public void FindNewest_returns_the_most_recently_modified_session_for_the_cwd()
    {
        using var temp = new TempDirectory();
        Session older = Create(temp.File("older.jsonl"), Cwd, Start);
        Session newer = Create(temp.File("newer.jsonl"), Cwd, Start.AddMinutes(1));
        Touch(older.Path, Start);
        Touch(newer.Path, Start.AddMinutes(5));

        SessionSummary? found = Session.FindNewest(temp.Root, Cwd, StringComparison.Ordinal);

        Assert.NotNull(found);
        Assert.Equal(newer.SessionId, found.Id);
        Assert.Equal(newer.Path, found.Path);
    }

    [Fact]
    public void FindNewest_ignores_a_newer_session_recorded_for_another_cwd()
    {
        using var temp = new TempDirectory();
        Session matching = Create(temp.File("matching.jsonl"), Cwd, Start);
        Session other = Create(temp.File("other.jsonl"), "/elsewhere", Start);
        Touch(matching.Path, Start);
        Touch(other.Path, Start.AddHours(1));

        SessionSummary? found = Session.FindNewest(temp.Root, Cwd, StringComparison.Ordinal);

        Assert.NotNull(found);
        Assert.Equal(matching.SessionId, found.Id);
    }

    [Fact]
    public void FindNewest_returns_null_when_no_session_records_the_cwd()
    {
        using var temp = new TempDirectory();
        Create(temp.File("other.jsonl"), "/elsewhere", Start);

        Assert.Null(Session.FindNewest(temp.Root, Cwd, StringComparison.Ordinal));
    }

    [Fact]
    public void FindNewest_returns_null_for_a_missing_sessions_root()
    {
        using var temp = new TempDirectory();

        Assert.Null(Session.FindNewest(temp.File("missing"), Cwd, StringComparison.Ordinal));
    }

    [Fact]
    public void FindNewest_honours_the_requested_cwd_comparison()
    {
        using var temp = new TempDirectory();
        Session session = Create(temp.File("s.jsonl"), "/WORK", Start);

        Assert.Null(Session.FindNewest(temp.Root, Cwd, StringComparison.Ordinal));
        Assert.Equal(
            session.SessionId,
            Session.FindNewest(temp.Root, Cwd, StringComparison.OrdinalIgnoreCase)?.Id
        );
    }

    [Fact]
    public void FindNewest_walks_nested_session_directories()
    {
        using var temp = new TempDirectory();
        Session session = Create(temp.File(Path.Combine("nested", "deep", "s.jsonl")), Cwd, Start);

        SessionSummary? found = Session.FindNewest(temp.Root, Cwd, StringComparison.Ordinal);

        Assert.NotNull(found);
        Assert.Equal(session.SessionId, found.Id);
    }

    [Fact]
    public void FindNewest_tie_breaks_on_the_created_time_descending()
    {
        using var temp = new TempDirectory();
        Session older = Create(temp.File("older.jsonl"), Cwd, Start);
        Session newer = Create(temp.File("newer.jsonl"), Cwd, Start.AddMinutes(5));
        Touch(older.Path, Start);
        Touch(newer.Path, Start);

        SessionSummary? found = Session.FindNewest(temp.Root, Cwd, StringComparison.Ordinal);

        Assert.NotNull(found);
        Assert.Equal(newer.SessionId, found.Id);
    }

    [Fact]
    public void FindNewest_tie_breaks_on_the_id_descending()
    {
        using var temp = new TempDirectory();
        Session first = Create(temp.File("first.jsonl"), Cwd, Start);
        Session second = Create(temp.File("second.jsonl"), Cwd, Start);
        Touch(first.Path, Start);
        Touch(second.Path, Start);
        string expected =
            string.CompareOrdinal(first.SessionId, second.SessionId) > 0
                ? first.SessionId
                : second.SessionId;

        SessionSummary? found = Session.FindNewest(temp.Root, Cwd, StringComparison.Ordinal);

        Assert.NotNull(found);
        Assert.Equal(expected, found.Id);
    }

    [Fact]
    public void FindNewest_reads_only_the_header_line()
    {
        using var temp = new TempDirectory();
        Session session = Create(temp.File("s.jsonl"), Cwd, Start);
        File.AppendAllText(session.Path, "this tail line is corrupt\n");

        SessionSummary? found = Session.FindNewest(temp.Root, Cwd, StringComparison.Ordinal);

        Assert.NotNull(found);
        Assert.Equal(session.SessionId, found.Id);
    }

    [Fact]
    public async Task The_session_walk_ignores_a_directory_symlink_loop()
    {
        using var temp = new TempDirectory();
        Session session = Create(temp.File("s.jsonl"), Cwd, Start);
        if (!TryCreateDirectoryLoop(temp))
        {
            return;
        }

        SessionSummary? found = await Task.Run(() =>
                Session.FindNewest(temp.Root, Cwd, StringComparison.Ordinal)
            )
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        IReadOnlyList<SessionSummary> matches = await Task.Run(() =>
                Session.FindById(temp.Root, session.SessionId)
            )
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal(session.SessionId, found.Id);
        Assert.Single(matches);
    }

    [Fact]
    public void FindById_returns_the_matching_session()
    {
        using var temp = new TempDirectory();
        Session session = Create(temp.File("arbitrary-name.jsonl"), Cwd, Start);
        Create(temp.File("other.jsonl"), Cwd, Start);

        IReadOnlyList<SessionSummary> matches = Session.FindById(temp.Root, session.SessionId);

        SessionSummary match = Assert.Single(matches);
        Assert.Equal(session.SessionId, match.Id);
        Assert.Equal(session.Path, match.Path);
    }

    [Fact]
    public void FindById_names_every_match_of_an_ambiguous_id()
    {
        using var temp = new TempDirectory();
        Session session = Create(temp.File("original.jsonl"), Cwd, Start);
        string copy = temp.File(Path.Combine("nested", "copy.jsonl"));
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(session.Path, copy);
        Touch(session.Path, Start.AddMinutes(1));
        Touch(copy, Start.AddMinutes(2));

        IReadOnlyList<SessionSummary> matches = Session.FindById(temp.Root, session.SessionId);

        Assert.Equal(2, matches.Count);
        Assert.Equal(
            [copy, session.Path],
            matches.Select(match => match.Path).OrderBy(path => path, StringComparer.Ordinal)
        );
    }

    [Fact]
    public void FindById_returns_nothing_for_an_unknown_id()
    {
        using var temp = new TempDirectory();
        Create(temp.File("s.jsonl"), Cwd, Start);

        Assert.Empty(Session.FindById(temp.Root, "s_unknown"));
        Assert.Empty(Session.FindById(temp.File("missing"), "s_unknown"));
    }

    [Fact]
    public void Selection_never_modifies_the_session_files()
    {
        using var temp = new TempDirectory();
        Session session = Create(temp.File("s.jsonl"), Cwd, Start);
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));
        byte[] before = File.ReadAllBytes(session.Path);

        _ = Session.FindNewest(temp.Root, Cwd, StringComparison.Ordinal);
        _ = Session.FindById(temp.Root, session.SessionId);

        Assert.Equal(before, File.ReadAllBytes(session.Path));
    }

    private static Session Create(string path, string cwd, DateTimeOffset created) =>
        Session.Create(path, cwd, new FixedTimeProvider(created));

    private static bool TryCreateDirectoryLoop(TempDirectory temp)
    {
        // Windows may refuse symlink creation without Developer Mode or elevation; the walk's
        // reparse-point rule is platform-neutral, so the test stays applicable elsewhere.
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            Directory.CreateSymbolicLink(temp.File("loop"), temp.Root);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static void Touch(string path, DateTimeOffset modified) =>
        File.SetLastWriteTimeUtc(path, modified.UtcDateTime);
}

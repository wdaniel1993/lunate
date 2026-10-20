using System.Text;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class SessionTitleTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("  Refactor   the parser  ", "Refactor the parser")]
    [InlineData("one\ttwo\nthree", "one two three")]
    [InlineData("plain", "plain")]
    public void Normalize_trims_and_collapses_interior_whitespace(string input, string expected) =>
        Assert.Equal(expected, SessionTitle.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   \t\n ")]
    [InlineData(null)]
    public void Normalize_returns_null_for_a_blank_title(string? input) =>
        Assert.Null(SessionTitle.Normalize(input));

    [Fact]
    public void Normalize_accepts_a_title_of_the_maximum_length()
    {
        string title = new('a', SessionTitle.MaxLength);

        Assert.Equal(title, SessionTitle.Normalize(title));
    }

    [Fact]
    public void Normalize_refuses_a_title_over_the_maximum_length()
    {
        string title = new('a', SessionTitle.MaxLength + 1);

        Assert.Null(SessionTitle.Normalize(title));
    }

    [Fact]
    public void PathFor_places_the_sidecar_beside_the_session_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File(SessionPaths.SessionFileName("s_20261020-120000-abcd"));

        Assert.Equal(temp.File("s_20261020-120000-abcd.title"), SessionTitle.PathFor(path));
    }

    [Fact]
    public void Write_and_read_round_trip_the_title_by_id()
    {
        using var temp = new TempDirectory();
        Session session = CreateStoredSession(temp);

        SessionTitle.Write(session.Path, "  Refactor   the parser ");

        Assert.Equal("Refactor the parser", SessionTitle.Read(session.Path));
        Assert.True(File.Exists(temp.File(session.SessionId + ".title")));
    }

    [Fact]
    public void Write_stores_one_utf8_line_without_a_bom()
    {
        using var temp = new TempDirectory();
        Session session = CreateStoredSession(temp);

        SessionTitle.Write(session.Path, "Titel mit Ümlaut");

        byte[] bytes = File.ReadAllBytes(SessionTitle.PathFor(session.Path));
        Assert.Equal(new UTF8Encoding(false).GetBytes("Titel mit Ümlaut\n"), bytes);
    }

    [Fact]
    public void Write_leaves_the_session_file_untouched()
    {
        using var temp = new TempDirectory();
        Session session = CreateStoredSession(temp);
        session.AppendMessage(new ChatMessage(ChatRole.User, "hello"));
        byte[] before = File.ReadAllBytes(session.Path);
        DateTime beforeWrite = File.GetLastWriteTimeUtc(session.Path);

        SessionTitle.Write(session.Path, "Refactor the parser");

        Assert.Equal(before, File.ReadAllBytes(session.Path));
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(session.Path));
    }

    [Fact]
    public void Read_returns_null_for_a_missing_sidecar()
    {
        using var temp = new TempDirectory();

        Assert.Null(SessionTitle.Read(temp.File("missing.jsonl")));
    }

    [Fact]
    public void Read_returns_null_for_an_empty_sidecar()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        File.WriteAllText(SessionTitle.PathFor(path), "\n");

        Assert.Null(SessionTitle.Read(path));
    }

    [Fact]
    public void Read_returns_null_for_a_sidecar_with_invalid_utf8()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        File.WriteAllBytes(SessionTitle.PathFor(path), [0xC3, 0x28]);

        Assert.Null(SessionTitle.Read(path));
    }

    [Fact]
    public void Read_returns_null_for_an_over_long_sidecar()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        File.WriteAllText(SessionTitle.PathFor(path), new string('x', 121) + "\n");

        Assert.Null(SessionTitle.Read(path));
    }

    [Fact]
    public void Write_replaces_an_existing_title_without_leaving_a_temp_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");

        SessionTitle.Write(path, "first title");
        SessionTitle.Write(path, "second title");

        Assert.Equal("second title", SessionTitle.Read(path));
        Assert.Empty(Directory.GetFiles(temp.Root, "*.tmp"));
    }

    [Fact]
    public void Write_refuses_an_invalid_title()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");

        Assert.Throws<ArgumentException>(() =>
            SessionTitle.Write(path, new string('x', SessionTitle.MaxLength + 1))
        );
        Assert.False(File.Exists(SessionTitle.PathFor(path)));
    }

    private static Session CreateStoredSession(TempDirectory temp)
    {
        string provisional = temp.File(Path.GetRandomFileName());
        Session created = Session.Create(provisional, "/work", new FixedTimeProvider(Start));
        string path = temp.File(SessionPaths.SessionFileName(created.SessionId));
        File.Move(provisional, path);
        return Session.Load(path);
    }
}

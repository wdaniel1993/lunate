using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class SessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_writes_a_header_and_assigns_a_stamped_session_id()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");

        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));

        Assert.Matches("^s_20261006-120000-[0-9a-f]{4}$", session.SessionId);
        Assert.Equal(path, session.Path);
        Assert.Empty(session.Entries);

        SessionHeaderEntry header = Assert.IsType<SessionHeaderEntry>(
            SessionFormat.Parse(Assert.Single(ReadLines(path)))
        );
        Assert.Equal(session.SessionId, header.Id);
        Assert.Equal(1, header.Schema);
        Assert.Equal("/work", header.Cwd);
        Assert.Equal(Start, header.Created);
        Assert.False(string.IsNullOrWhiteSpace(header.Meai));
    }

    [Fact]
    public void Appends_assign_ids_the_parent_chain_and_timestamps()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );

        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));
        session.AppendMessage(
            new ChatMessage(ChatRole.Assistant, "two"),
            "gpt-4o-mini",
            new SessionUsage(5, 2)
        );
        session.AppendCompaction("summary", ["e_01", "e_02"]);
        session.AppendModelChange("gpt-4o");

        Assert.Equal(["e_01", "e_02", "e_03", "e_04"], session.Entries.Select(entry => entry.Id));
        Assert.Null(Assert.IsType<SessionMessageEntry>(session.Entries[0]).ParentId);
        Assert.Equal("e_01", Assert.IsType<SessionMessageEntry>(session.Entries[1]).ParentId);
        Assert.Equal("e_02", Assert.IsType<SessionCompactionEntry>(session.Entries[2]).ParentId);
        Assert.Equal("e_03", Assert.IsType<SessionModelChangeEntry>(session.Entries[3]).ParentId);
        Assert.All(session.Entries, entry => Assert.Equal(Start, TimestampOf(entry)));

        SessionMessageEntry assistant = Assert.IsType<SessionMessageEntry>(session.Entries[1]);
        Assert.Equal("gpt-4o-mini", assistant.Model);
        Assert.Equal(new SessionUsage(5, 2), assistant.Usage);
    }

    [Fact]
    public void Append_and_load_round_trip_the_entries_and_history()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session created = Session.Create(path, "/work", new FixedTimeProvider(Start));
        created.AppendMessage(new ChatMessage(ChatRole.User, "one"));
        created.AppendMessage(
            new ChatMessage(ChatRole.Assistant, "two"),
            "gpt-4o-mini",
            new SessionUsage(5, 2)
        );
        created.AppendCompaction("summary", ["e_01"]);
        created.AppendMessage(
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "ok")])
        );
        created.AppendMessage(new ChatMessage(ChatRole.User, "three"));

        Session loaded = Session.Load(path, new FixedTimeProvider(Start));

        Assert.Equal(created.SessionId, loaded.SessionId);
        Assert.Equal(path, loaded.Path);
        Assert.Equal(
            created.Entries.Select(SessionFormat.Serialize),
            loaded.Entries.Select(SessionFormat.Serialize)
        );
        List<ChatMessage> history = loaded.ToHistory();
        Assert.Equal(
            ["user", "assistant", "tool", "user"],
            history.Select(message => message.Role.Value)
        );
        Assert.Equal("one", history[0].Text);
        Assert.Equal("two", history[1].Text);
        Assert.Equal(
            "ok",
            Assert
                .IsType<FunctionResultContent>(Assert.Single(history[2].Contents))
                .Result!.ToString()
        );
        Assert.Equal("three", history[3].Text);
        Assert.Equal(
            new SessionUsage(5, 2),
            Assert.IsType<SessionMessageEntry>(loaded.Entries[1]).Usage
        );

        loaded.AppendMessage(new ChatMessage(ChatRole.User, "four"));
        Assert.Equal("e_06", loaded.Entries[^1].Id);
        SessionMessageEntry appended = Assert.IsType<SessionMessageEntry>(loaded.Entries[^1]);
        Assert.Equal("e_05", appended.ParentId);
        Assert.Equal("four", appended.Message.Text);
    }

    [Fact]
    public void Load_fails_actionably_for_an_unknown_schema()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        File.WriteAllText(
            path,
            """{"type":"header","schema":99,"id":"s_x","cwd":"/work","created":"2026-10-06T12:00:00.0000000+00:00","meai":"10.10.1"}"""
                + "\n"
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            Session.Load(path)
        );

        Assert.Contains("99", exception.Message);
        Assert.Contains(path, exception.Message);
    }

    [Fact]
    public void Load_fails_actionably_when_the_header_is_missing()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        File.WriteAllText(
            path,
            """{"type":"message","id":"e_01","parentId":null,"timestamp":"2026-10-06T12:00:00.0000000+00:00","message":{"role":"user","contents":[{"$type":"text","text":"Hi"}]}}"""
                + "\n"
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            Session.Load(path)
        );

        Assert.Contains(path, exception.Message);
        Assert.Contains("header", exception.Message);
    }

    [Fact]
    public void Load_fails_actionably_for_malformed_json()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        File.WriteAllText(path, "{not json}\n");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            Session.Load(path)
        );

        Assert.Contains(path, exception.Message);
    }

    [Fact]
    public void Load_fails_actionably_for_an_empty_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        File.WriteAllText(path, string.Empty);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            Session.Load(path)
        );

        Assert.Contains(path, exception.Message);
    }

    [Fact]
    public void Lines_are_written_with_lf_endings_and_no_bom()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));

        byte[] bytes = File.ReadAllBytes(path);

        Assert.False(bytes.AsSpan().StartsWith("\uFEFF"u8));
        Assert.Equal(-1, Array.IndexOf(bytes, (byte)'\r'));
        Assert.Equal((byte)'\n', bytes[^1]);
    }

    private static string[] ReadLines(string path) =>
        File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static DateTimeOffset TimestampOf(SessionEntry entry) =>
        entry switch
        {
            SessionMessageEntry message => message.Timestamp,
            SessionCompactionEntry compaction => compaction.Timestamp,
            SessionModelChangeEntry change => change.Timestamp,
            _ => throw new InvalidOperationException($"No timestamp on {entry.GetType().Name}."),
        };
}

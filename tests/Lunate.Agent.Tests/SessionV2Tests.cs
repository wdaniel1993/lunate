using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>Schema 2 store behavior: creation, append methods, bounds and v1 load compatibility.</summary>
public sealed class SessionV2Tests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_writes_schema_2_with_repo_and_worktree()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");

        Session session = Session.Create(
            path,
            "/work",
            "github.com/acme/widgets",
            "/work/trees/a",
            new FixedTimeProvider(Start)
        );

        SessionHeaderEntry header = HeaderOf(path);
        Assert.Equal(2, SessionFormat.SchemaVersion);
        Assert.Equal(2, header.Schema);
        Assert.Equal(session.SessionId, header.Id);
        Assert.Equal("github.com/acme/widgets", header.Repo);
        Assert.Equal("/work/trees/a", header.Worktree);
    }

    [Fact]
    public void Create_without_repo_writes_a_schema_2_header_without_them()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");

        Session.Create(path, "/work", new FixedTimeProvider(Start));

        SessionHeaderEntry header = HeaderOf(path);
        Assert.Equal(2, header.Schema);
        Assert.Null(header.Repo);
        Assert.Null(header.Worktree);
    }

    [Fact]
    public void Append_methods_assign_ids_the_parent_chain_and_timestamps()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );

        session.AppendActiveTools(["read", "write"]);
        session.AppendPromptSection("tools", "You may call tools.");
        session.AppendChildSession("s_child", "r_child");
        session.AppendNestedCalls(
            "call-1",
            [new SessionNestedCall("read", """{"path":"a.txt"}""", "ok", 7)]
        );

        Assert.Equal(["e_01", "e_02", "e_03", "e_04"], session.Entries.Select(entry => entry.Id));
        var activeTools = Assert.IsType<SessionActiveToolsEntry>(session.Entries[0]);
        Assert.Null(activeTools.ParentId);
        Assert.Equal(["read", "write"], activeTools.Tools);
        var promptSection = Assert.IsType<SessionPromptSectionEntry>(session.Entries[1]);
        Assert.Equal("e_01", promptSection.ParentId);
        Assert.Equal("tools", promptSection.Section);
        var childSession = Assert.IsType<SessionChildSessionEntry>(session.Entries[2]);
        Assert.Equal("e_02", childSession.ParentId);
        Assert.Equal("s_child", childSession.ChildSessionId);
        var nestedCalls = Assert.IsType<SessionNestedCallsEntry>(session.Entries[3]);
        Assert.Equal("e_03", nestedCalls.ParentId);
        Assert.Equal("call-1", nestedCalls.CallId);
        Assert.Equal(
            new SessionNestedCall("read", """{"path":"a.txt"}""", "ok", 7),
            Assert.Single(nestedCalls.Calls)
        );
    }

    [Fact]
    public void Append_extension_round_trips_the_payload_byte_for_byte()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        const string payload = """{"zeta": 1, "text": "grüß"}""";
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));

        session.AppendExtension("ext/acme/note", payload);

        string line = File.ReadAllText(path).Split('\n')[1];
        Assert.Equal(
            """{"type":"ext/acme/note","id":"e_01","parentId":null,"timestamp":"2026-10-06T12:00:00.0000000+00:00","payload":{"zeta": 1, "text": "grüß"}}""",
            line
        );
        Session loaded = Session.Load(path, new FixedTimeProvider(Start));
        var extension = Assert.IsType<SessionExtensionEntry>(Assert.Single(loaded.Entries));
        Assert.Equal(payload, extension.PayloadJson);
    }

    [Theory]
    [InlineData("ext/only")]
    [InlineData("notes")]
    public void Append_extension_rejects_a_malformed_extension_type(string type)
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );

        Assert.Throws<ArgumentException>(() => session.AppendExtension(type, "{}"));
    }

    [Fact]
    public void Append_extension_rejects_invalid_payload_json()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );

        Assert.Throws<ArgumentException>(() => session.AppendExtension("ext/acme/note", "{oops}"));
    }

    [Fact]
    public void Nested_calls_truncate_arguments_at_two_hundred_characters()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        string kept = new('a', 200);
        string truncated = new('b', 201);

        session.AppendNestedCalls(
            "call-1",
            [
                new SessionNestedCall("read", kept, "ok", 1),
                new SessionNestedCall("read", truncated, "ok", 1),
            ]
        );

        SessionNestedCallsEntry entry = Assert.IsType<SessionNestedCallsEntry>(
            Assert.Single(session.Entries)
        );
        Assert.Equal(kept, entry.Calls[0].Args);
        Assert.Equal(new string('b', 200) + "…", entry.Calls[1].Args);
    }

    [Fact]
    public void Nested_calls_keep_at_most_thirty_two_calls()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        SessionNestedCall[] calls =
        [
            .. Enumerable
                .Range(0, 33)
                .Select(index => new SessionNestedCall($"t{index}", "{}", "ok", index)),
        ];

        session.AppendNestedCalls("call-1", calls);

        SessionNestedCallsEntry entry = Assert.IsType<SessionNestedCallsEntry>(
            Assert.Single(session.Entries)
        );
        Assert.Equal(32, entry.Calls.Count);
        Assert.Equal("t0", entry.Calls[0].Name);
        Assert.Equal("t31", entry.Calls[^1].Name);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("error")]
    [InlineData("cancelled")]
    public void Nested_calls_accept_the_three_statuses(string status)
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );

        session.AppendNestedCalls("call-1", [new SessionNestedCall("read", "{}", status, 1)]);

        SessionNestedCallsEntry entry = Assert.IsType<SessionNestedCallsEntry>(
            Assert.Single(session.Entries)
        );
        Assert.Equal(status, entry.Calls[0].Status);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("OK")]
    [InlineData("")]
    public void Nested_calls_reject_invalid_statuses(string status)
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );

        Assert.Throws<ArgumentException>(() =>
            session.AppendNestedCalls("call-1", [new SessionNestedCall("read", "{}", status, 1)])
        );
    }

    [Fact]
    public void Append_and_load_round_trip_every_entry_kind()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session created = Session.Create(
            path,
            "/work",
            "github.com/acme/widgets",
            "/work/trees/a",
            new FixedTimeProvider(Start)
        );
        created.AppendMessage(new ChatMessage(ChatRole.User, "one"));
        created.AppendActiveTools(["read", "write"]);
        created.AppendPromptSection("tools", "You may call tools.");
        created.AppendChildSession("s_child", "r_child");
        created.AppendNestedCalls(
            "call-1",
            [new SessionNestedCall("read", """{"path":"a.txt"}""", "ok", 7)]
        );
        created.AppendExtension("ext/acme/note", """{"zeta": 1}""");
        created.AppendCompaction("summary", ["e_01"]);
        created.AppendModelChange("gpt-4o");

        Session loaded = Session.Load(path, new FixedTimeProvider(Start));

        Assert.Equal(created.SessionId, loaded.SessionId);
        Assert.Equal(
            created.Entries.Select(SessionFormat.Serialize),
            loaded.Entries.Select(SessionFormat.Serialize)
        );
        Assert.Single(loaded.ToHistory());
        Assert.Equal("one", loaded.ToHistory()[0].Text);
    }

    [Fact]
    public void A_schema_1_session_loads_with_v1_semantics()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        File.WriteAllText(
            path,
            """{"type":"header","schema":1,"id":"s_x","cwd":"/work","created":"2026-10-06T12:00:00.0000000+00:00","meai":"10.10.1"}"""
                + "\n"
                + """{"type":"message","id":"e_01","parentId":null,"timestamp":"2026-10-06T12:00:01.0000000+00:00","message":{"role":"user","contents":[{"$type":"text","text":"Hi"}]}}"""
                + "\n"
        );

        Session loaded = Session.Load(path, new FixedTimeProvider(Start));

        SessionHeaderEntry header = HeaderOf(path);
        Assert.Equal(1, header.Schema);
        Assert.Null(header.Repo);
        Assert.Null(header.Worktree);
        SessionMessageEntry message = Assert.IsType<SessionMessageEntry>(
            Assert.Single(loaded.Entries)
        );
        Assert.Null(message.RawMessageJson);
        Assert.Equal("Hi", message.Message.Text);
    }

    private static SessionHeaderEntry HeaderOf(string path) =>
        Assert.IsType<SessionHeaderEntry>(
            SessionFormat.Parse(File.ReadAllText(path).Split('\n')[0])
        );
}

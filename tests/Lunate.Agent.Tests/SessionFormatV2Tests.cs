using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>Schema 2 line shapes: new entries, extension payloads and unknown-content preservation.</summary>
public sealed class SessionFormatV2Tests
{
    private static readonly DateTimeOffset Recorded = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private const string TrickyPayload =
        """{"zeta": 1, "text": "grüß", "nested": {  "b": [1, 2], "a": null }}""";

    [Fact]
    public void Header_with_repo_and_worktree_serializes_them_after_meai()
    {
        var header = new SessionHeaderEntry(
            "s_20261006-120000-abcd",
            2,
            "/work",
            Recorded,
            "10.10.1",
            "github.com/acme/widgets",
            "/work/trees/a"
        );

        string line = SessionFormat.Serialize(header);

        Assert.Equal(
            """{"type":"header","schema":2,"id":"s_20261006-120000-abcd","cwd":"/work","created":"2026-10-06T12:00:00.0000000+00:00","meai":"10.10.1","repo":"github.com/acme/widgets","worktree":"/work/trees/a"}""",
            line
        );
        SessionHeaderEntry parsed = Assert.IsType<SessionHeaderEntry>(SessionFormat.Parse(line));
        Assert.Equal("github.com/acme/widgets", parsed.Repo);
        Assert.Equal("/work/trees/a", parsed.Worktree);
    }

    [Fact]
    public void Header_without_repo_and_worktree_omits_both_fields()
    {
        var header = new SessionHeaderEntry("s_x", 2, "/work", Recorded, "10.10.1");

        string line = SessionFormat.Serialize(header);

        Assert.Equal(
            """{"type":"header","schema":2,"id":"s_x","cwd":"/work","created":"2026-10-06T12:00:00.0000000+00:00","meai":"10.10.1"}""",
            line
        );
        SessionHeaderEntry parsed = Assert.IsType<SessionHeaderEntry>(SessionFormat.Parse(line));
        Assert.Null(parsed.Repo);
        Assert.Null(parsed.Worktree);
    }

    [Fact]
    public void Schema_1_header_parses_with_null_repo_and_worktree()
    {
        SessionHeaderEntry parsed = Assert.IsType<SessionHeaderEntry>(
            SessionFormat.Parse(
                """{"type":"header","schema":1,"id":"s_x","cwd":"/work","created":"2026-10-06T12:00:00.0000000+00:00","meai":"10.10.1"}"""
            )
        );

        Assert.Equal(1, parsed.Schema);
        Assert.Null(parsed.Repo);
        Assert.Null(parsed.Worktree);
    }

    [Fact]
    public void Active_tools_entry_round_trips()
    {
        var entry = new SessionActiveToolsEntry("e_01", null, Recorded, ["read", "write"]);

        string line = SessionFormat.Serialize(entry);

        Assert.Equal(
            """{"type":"activeTools","id":"e_01","parentId":null,"timestamp":"2026-10-06T12:00:00.0000000+00:00","tools":["read","write"]}""",
            line
        );
        SessionActiveToolsEntry parsed = Assert.IsType<SessionActiveToolsEntry>(
            SessionFormat.Parse(line)
        );
        Assert.Equal(["read", "write"], parsed.Tools);
    }

    [Fact]
    public void Prompt_section_entry_round_trips()
    {
        var entry = new SessionPromptSectionEntry(
            "e_02",
            "e_01",
            Recorded,
            "tools",
            "You may call tools."
        );

        string line = SessionFormat.Serialize(entry);

        Assert.Equal(
            """{"type":"promptSection","id":"e_02","parentId":"e_01","timestamp":"2026-10-06T12:00:00.0000000+00:00","section":"tools","text":"You may call tools."}""",
            line
        );
        SessionPromptSectionEntry parsed = Assert.IsType<SessionPromptSectionEntry>(
            SessionFormat.Parse(line)
        );
        Assert.Equal("tools", parsed.Section);
        Assert.Equal("You may call tools.", parsed.Text);
    }

    [Fact]
    public void Child_session_entry_round_trips()
    {
        var entry = new SessionChildSessionEntry("e_03", "e_02", Recorded, "s_child", "r_child");

        string line = SessionFormat.Serialize(entry);

        Assert.Equal(
            """{"type":"childSession","id":"e_03","parentId":"e_02","timestamp":"2026-10-06T12:00:00.0000000+00:00","childSessionId":"s_child","runId":"r_child"}""",
            line
        );
        SessionChildSessionEntry parsed = Assert.IsType<SessionChildSessionEntry>(
            SessionFormat.Parse(line)
        );
        Assert.Equal("s_child", parsed.ChildSessionId);
        Assert.Equal("r_child", parsed.RunId);
    }

    [Fact]
    public void Nested_calls_entry_round_trips()
    {
        var entry = new SessionNestedCallsEntry(
            "e_04",
            "e_03",
            Recorded,
            "call-1",
            [
                new SessionNestedCall("read", """{"path":"a.txt"}""", "ok", 12),
                new SessionNestedCall("write", "{}", "error", 3),
            ]
        );

        string line = SessionFormat.Serialize(entry);

        Assert.Equal(
            """{"type":"nestedCalls","id":"e_04","parentId":"e_03","timestamp":"2026-10-06T12:00:00.0000000+00:00","callId":"call-1","calls":[{"name":"read","args":"{\"path\":\"a.txt\"}","status":"ok","durationMs":12},{"name":"write","args":"{}","status":"error","durationMs":3}]}""",
            line
        );
        SessionNestedCallsEntry parsed = Assert.IsType<SessionNestedCallsEntry>(
            SessionFormat.Parse(line)
        );
        Assert.Equal("call-1", parsed.CallId);
        Assert.Equal(2, parsed.Calls.Count);
        Assert.Equal(
            new SessionNestedCall("read", """{"path":"a.txt"}""", "ok", 12),
            parsed.Calls[0]
        );
        Assert.Equal(new SessionNestedCall("write", "{}", "error", 3), parsed.Calls[1]);
    }

    [Fact]
    public void Extension_entry_re_emits_the_payload_byte_for_byte()
    {
        var entry = new SessionExtensionEntry(
            "e_05",
            null,
            Recorded,
            "ext/acme/note",
            TrickyPayload
        );

        string line = SessionFormat.Serialize(entry);

        Assert.Equal(
            """{"type":"ext/acme/note","id":"e_05","parentId":null,"timestamp":"2026-10-06T12:00:00.0000000+00:00","payload":{"zeta": 1, "text": "grüß", "nested": {  "b": [1, 2], "a": null }}}""",
            line
        );
        SessionExtensionEntry parsed = Assert.IsType<SessionExtensionEntry>(
            SessionFormat.Parse(line)
        );
        Assert.Equal("ext/acme/note", parsed.ExtensionType);
        Assert.Equal(TrickyPayload, parsed.PayloadJson);
        Assert.Equal(line, SessionFormat.Serialize(parsed));
    }

    [Fact]
    public void Extension_payload_node_is_parsed_on_access()
    {
        var entry = new SessionExtensionEntry(
            "e_05",
            null,
            Recorded,
            "ext/acme/note",
            TrickyPayload
        );

        JsonNode? payload = entry.Payload;

        Assert.NotNull(payload);
        Assert.Equal("grüß", (string?)payload!["text"]);
        Assert.Equal(1, (int)payload!["zeta"]!);
    }

    [Theory]
    [InlineData("ext/only")]
    [InlineData("ext/")]
    [InlineData("ext/acme")]
    public void Parse_rejects_a_malformed_extension_type_naming_it(string type)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SessionFormat.Parse(
                $$"""{"type":"{{type}}","id":"e_01","parentId":null,"timestamp":"2026-10-06T12:00:00.0000000+00:00","payload":{{"{}"}}}"""
            )
        );

        Assert.Contains(type, exception.Message);
    }

    [Fact]
    public void An_extension_type_with_more_than_three_segments_is_valid()
    {
        var entry = new SessionExtensionEntry("e_01", null, Recorded, "ext/acme/note/extra", "{}");

        SessionExtensionEntry parsed = Assert.IsType<SessionExtensionEntry>(
            SessionFormat.Parse(SessionFormat.Serialize(entry))
        );

        Assert.Equal("ext/acme/note/extra", parsed.ExtensionType);
    }

    [Fact]
    public void A_message_whose_content_does_not_re_serialize_keeps_its_raw_json()
    {
        string line =
            """{"type":"message","id":"e_01","parentId":null,"timestamp":"2026-10-06T12:00:00.0000000+00:00","message":{"role":"assistant","contents":[{"$type":"webSearchToolResult","callId":"call_w","result":[{"title":"Result","url":"https://example.com"}]}]}}""";

        SessionMessageEntry parsed = Assert.IsType<SessionMessageEntry>(SessionFormat.Parse(line));

        Assert.NotNull(parsed.RawMessageJson);
        Assert.Equal(line, SessionFormat.Serialize(parsed));
    }

    [Fact]
    public void A_message_that_re_serializes_deep_equal_keeps_no_raw_json()
    {
        var entry = new SessionMessageEntry(
            "e_01",
            null,
            Recorded,
            new ChatMessage(ChatRole.User, "Hi"),
            null,
            null
        );

        SessionMessageEntry parsed = Assert.IsType<SessionMessageEntry>(
            SessionFormat.Parse(SessionFormat.Serialize(entry))
        );

        Assert.Null(parsed.RawMessageJson);
        Assert.Equal("Hi", parsed.Message.Text);
    }
}

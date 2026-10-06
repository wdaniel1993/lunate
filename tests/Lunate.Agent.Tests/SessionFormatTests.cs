using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class SessionFormatTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Header_serializes_in_the_fixed_property_order_with_a_utc_timestamp()
    {
        var header = new SessionHeaderEntry(
            "s_20261006-120000-abcd",
            1,
            "/work",
            Recorded,
            "10.10.1"
        );

        string line = SessionFormat.Serialize(header);

        Assert.Equal(
            """{"type":"header","schema":1,"id":"s_20261006-120000-abcd","cwd":"/work","created":"2026-10-06T12:00:00.0000000+00:00","meai":"10.10.1"}""",
            line
        );
    }

    [Fact]
    public void Message_entry_serializes_the_envelope_and_the_meai_message_node()
    {
        var entry = new SessionMessageEntry(
            "e_02",
            "e_01",
            Recorded,
            new ChatMessage(ChatRole.Assistant, "Hello"),
            "gpt-4o-mini",
            new SessionUsage(7, 3)
        );

        string line = SessionFormat.Serialize(entry);

        Assert.Equal(
            """{"type":"message","id":"e_02","parentId":"e_01","timestamp":"2026-10-06T12:00:00.0000000+00:00","message":{"role":"assistant","contents":[{"$type":"text","text":"Hello"}]},"model":"gpt-4o-mini","usage":{"input":7,"output":3}}""",
            line
        );
    }

    [Fact]
    public void Message_entry_keeps_parentId_null_and_omits_unknown_model_and_usage()
    {
        var entry = new SessionMessageEntry(
            "e_01",
            null,
            Recorded,
            new ChatMessage(ChatRole.User, "Hi"),
            null,
            null
        );

        string line = SessionFormat.Serialize(entry);

        Assert.Equal(
            """{"type":"message","id":"e_01","parentId":null,"timestamp":"2026-10-06T12:00:00.0000000+00:00","message":{"role":"user","contents":[{"$type":"text","text":"Hi"}]}}""",
            line
        );
    }

    [Fact]
    public void Timestamps_are_normalized_to_utc()
    {
        var offset = new DateTimeOffset(2026, 10, 6, 14, 0, 0, TimeSpan.FromHours(2));
        var entry = new SessionMessageEntry(
            "e_01",
            null,
            offset,
            new ChatMessage(ChatRole.User, "Hi"),
            null,
            null
        );

        string line = SessionFormat.Serialize(entry);

        Assert.Contains("\"timestamp\":\"2026-10-06T12:00:00.0000000+00:00\"", line);
        SessionMessageEntry parsed = Assert.IsType<SessionMessageEntry>(SessionFormat.Parse(line));
        Assert.Equal(offset, parsed.Timestamp);
    }

    [Fact]
    public void Compaction_entry_round_trips()
    {
        var entry = new SessionCompactionEntry(
            "e_04",
            "e_03",
            Recorded,
            "Summary",
            ["e_01", "e_02"]
        );

        string line = SessionFormat.Serialize(entry);

        Assert.Equal(
            """{"type":"compaction","id":"e_04","parentId":"e_03","timestamp":"2026-10-06T12:00:00.0000000+00:00","summary":"Summary","replaces":["e_01","e_02"]}""",
            line
        );
        SessionCompactionEntry parsed = Assert.IsType<SessionCompactionEntry>(
            SessionFormat.Parse(line)
        );
        Assert.Equal("e_04", parsed.Id);
        Assert.Equal("e_03", parsed.ParentId);
        Assert.Equal(Recorded, parsed.Timestamp);
        Assert.Equal("Summary", parsed.Summary);
        Assert.Equal(["e_01", "e_02"], parsed.Replaces);
    }

    [Fact]
    public void Model_change_entry_round_trips()
    {
        var entry = new SessionModelChangeEntry("e_05", "e_04", Recorded, "gpt-4o");

        string line = SessionFormat.Serialize(entry);

        Assert.Equal(
            """{"type":"modelChange","id":"e_05","parentId":"e_04","timestamp":"2026-10-06T12:00:00.0000000+00:00","model":"gpt-4o"}""",
            line
        );
        SessionModelChangeEntry parsed = Assert.IsType<SessionModelChangeEntry>(
            SessionFormat.Parse(line)
        );
        Assert.Equal(entry, parsed);
    }

    [Fact]
    public void Message_entry_with_a_tool_result_round_trips()
    {
        var message = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "ok")]);
        var entry = new SessionMessageEntry("e_03", "e_02", Recorded, message, null, null);

        SessionMessageEntry parsed = Assert.IsType<SessionMessageEntry>(
            SessionFormat.Parse(SessionFormat.Serialize(entry))
        );

        Assert.Equal(ChatRole.Tool, parsed.Message.Role);
        var result = Assert.IsType<FunctionResultContent>(Assert.Single(parsed.Message.Contents));
        Assert.Equal("call-1", result.CallId);
        Assert.Equal("ok", result.Result!.ToString());
    }

    [Fact]
    public void Parse_rejects_an_unknown_entry_type_actionably()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SessionFormat.Parse("""{"type":"bogus","id":"e_01"}""")
        );

        Assert.Contains("bogus", exception.Message);
    }

    [Fact]
    public void Parse_rejects_invalid_json_actionably()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SessionFormat.Parse("not json")
        );

        Assert.Contains("JSON", exception.Message);
    }

    [Fact]
    public void Parse_rejects_a_message_without_the_embedded_message()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SessionFormat.Parse(
                """{"type":"message","id":"e_01","parentId":null,"timestamp":"2026-10-06T12:00:00.0000000+00:00"}"""
            )
        );

        Assert.Contains("message", exception.Message);
    }

    [Fact]
    public void Parse_rejects_a_line_without_a_type()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SessionFormat.Parse("""{"id":"e_01"}""")
        );

        Assert.Contains("type", exception.Message);
    }
}

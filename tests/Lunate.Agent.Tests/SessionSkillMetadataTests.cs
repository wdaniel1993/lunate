using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>The skill metadata on an invoked skill's user turn: serialization shape, parse
/// round-trip and the append path.</summary>
public sealed class SessionSkillMetadataTests
{
    [Fact]
    public void A_skill_turn_serializes_the_metadata_after_the_usage()
    {
        var entry = new SessionMessageEntry(
            "e_01",
            null,
            new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero),
            new ChatMessage(ChatRole.User, "body"),
            null,
            new SessionUsage(3, 4)
        )
        {
            Skill = new SessionSkillMetadata("greek", "/skills/greek"),
        };

        string line = SessionFormat.Serialize(entry);

        Assert.Contains(
            "\"skill\":{\"name\":\"greek\",\"dir\":\"/skills/greek\"}",
            line,
            StringComparison.Ordinal
        );
        SessionMessageEntry parsed = Assert.IsType<SessionMessageEntry>(SessionFormat.Parse(line));
        Assert.Equal(new SessionSkillMetadata("greek", "/skills/greek"), parsed.Skill);
    }

    [Fact]
    public void An_ordinary_turn_omits_the_skill_metadata()
    {
        var entry = new SessionMessageEntry(
            "e_01",
            null,
            new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero),
            new ChatMessage(ChatRole.User, "hello"),
            null,
            null
        );

        string line = SessionFormat.Serialize(entry);

        Assert.DoesNotContain("skill", line, StringComparison.Ordinal);
        SessionMessageEntry parsed = Assert.IsType<SessionMessageEntry>(SessionFormat.Parse(line));
        Assert.Null(parsed.Skill);
    }

    [Fact]
    public void AppendSkillMessage_records_the_user_entry_with_metadata()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("session.jsonl"),
            temp.Root,
            TimeProvider.System
        );

        session.AppendSkillMessage(
            new ChatMessage(ChatRole.User, "body"),
            new SessionSkillMetadata("greek", "/skills/greek")
        );

        SessionMessageEntry entry = Assert.IsType<SessionMessageEntry>(session.Entries[0]);
        Assert.Equal("greek", entry.Skill?.Name);
        Assert.Equal("/skills/greek", entry.Skill?.Dir);
        Session reloaded = Session.Load(session.Path);
        SessionMessageEntry parsed = Assert.IsType<SessionMessageEntry>(reloaded.Entries[0]);
        Assert.Equal("greek", parsed.Skill?.Name);
    }
}

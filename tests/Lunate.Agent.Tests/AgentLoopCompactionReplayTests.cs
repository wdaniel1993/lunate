namespace Lunate.Agent.Tests;

public sealed class AgentLoopCompactionReplayTests
{
    [Fact]
    public async Task The_long_session_compacts_once_and_the_next_request_is_under_60_percent()
    {
        using var temp = new TempDirectory();
        using var fixture = new TempDirectory();
        (ScriptedChatClient provider, Session session) = await AgentLoopCompaction.RecordAsync(
            temp,
            fixture.File("fresh.jsonl")
        );

        Assert.Equal(2, provider.Requests.Count);
        ScriptedRequest next = provider.Requests[1];
        long tokens = CompactionReducer.Utf8Length(next.Messages) / 4;
        Assert.True(
            tokens < AgentLoopCompaction.Window * 60 / 100,
            $"expected under 60% of the window, got {tokens} of {AgentLoopCompaction.Window}"
        );

        SessionCompactionEntry entry = Assert.Single(
            session.Entries.OfType<SessionCompactionEntry>()
        );
        Assert.Equal("goal: continue the recorded long session.", entry.Summary);
        Assert.Equal(["e_01", "e_02"], entry.Replaces);
    }

    [Fact]
    public async Task The_committed_long_session_fixture_replays_with_a_stable_snapshot()
    {
        using var temp = new TempDirectory();
        (List<AgentEvent> events, Session session) = await AgentLoopCompaction.ReplayAsync(temp);

        Assert.Empty(EventSequenceValidator.Validate(events));
        CompactionApplied applied = Assert.Single(events.OfType<CompactionApplied>());
        Assert.Equal(["e_01", "e_02"], applied.ReplacedEntryIds);
        Assert.Single(session.Entries.OfType<SessionCompactionEntry>());
        string expected = File.ReadAllText(AgentLoopCompaction.SnapshotPath).Replace("\r\n", "\n");
        Assert.Equal(expected, EventSequenceSnapshot.Serialize(events));
    }

    [Fact]
    public async Task A_fresh_recording_of_the_long_session_matches_the_committed_fixture()
    {
        using var temp = new TempDirectory();
        string fresh = temp.File("fresh.jsonl");

        await AgentLoopCompaction.RecordAsync(temp, fresh);

        string[] committed = File.ReadAllLines(AgentLoopCompaction.FixturePath);
        string[] recorded = File.ReadAllLines(fresh);
        Assert.Equal(committed.Length, recorded.Length);
        Assert.Equal(committed[1..], recorded[1..]);
    }
}

using System.Diagnostics;

namespace Lunate.Agent.Tests;

public sealed class AgentLoopReplayTests
{
    [Fact]
    public Task Text_only_session_replays_with_a_stable_snapshot_and_spans() =>
        AssertSession(AgentLoopSessions.TextOnly());

    [Fact]
    public Task Single_tool_session_replays_with_a_stable_snapshot_and_spans() =>
        AssertSession(AgentLoopSessions.SingleToolCall());

    [Fact]
    public Task Multi_step_session_replays_with_a_stable_snapshot_and_spans() =>
        AssertSession(AgentLoopSessions.MultiStep());

    [Fact]
    public async Task A_fresh_recording_of_every_session_matches_the_committed_fixture()
    {
        foreach (AgentLoopSession session in AgentLoopSessions.All)
        {
            using var temp = new TempDirectory();
            string fresh = temp.File(session.Name + ".jsonl");

            await AgentLoopReplay.RecordAsync(session, fresh);

            string[] committed = File.ReadAllLines(session.FixturePath);
            string[] recorded = File.ReadAllLines(fresh);
            Assert.Equal(committed.Length, recorded.Length);
            Assert.Equal(committed[1..], recorded[1..]);
        }
    }

    private static async Task AssertSession(AgentLoopSession session)
    {
        using var listener = new RecordingActivityListener();
        AgentHarness harness = AgentLoopReplay.CreateHarness(session);

        List<AgentEvent> events = await harness
            .RunAsync(session.UserInput, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(EventSequenceValidator.Validate(events));
        string expected = File.ReadAllText(session.SnapshotPath).Replace("\r\n", "\n");
        Assert.Equal(expected, EventSequenceSnapshot.Serialize(events));

        Activity run = Assert.Single(
            listener.Activities,
            activity => activity.DisplayName == "invoke_agent lunate"
        );
        List<Activity> modelSpans =
        [
            .. listener.Activities.Where(activity =>
                activity.Source.Name.Contains("Microsoft.Extensions.AI", StringComparison.Ordinal)
            ),
        ];
        Assert.Equal(session.ModelCalls, modelSpans.Count);
        Assert.All(modelSpans, span => Assert.Equal(run.SpanId, span.ParentSpanId));

        List<Activity> toolSpans =
        [
            .. listener.Activities.Where(activity =>
                activity.DisplayName.StartsWith("execute_tool ", StringComparison.Ordinal)
            ),
        ];
        Assert.Equal(session.ToolCalls, toolSpans.Count);
        Assert.All(toolSpans, span => Assert.Equal(run.SpanId, span.ParentSpanId));
    }
}

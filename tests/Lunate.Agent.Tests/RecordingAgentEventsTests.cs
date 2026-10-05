namespace Lunate.Agent.Tests;

public sealed class RecordingAgentEventsTests
{
    [Fact]
    public void Records_emitted_events_in_order()
    {
        var recording = new RecordingAgentEvents();
        AgentEvent started = new RunStarted("run_1");
        AgentEvent finished = new RunFinished("run_1", StopReasons.Stop);

        recording.Emit(started);
        recording.Emit(finished);

        Assert.Collection(
            recording.Events,
            agentEvent => Assert.Same(started, agentEvent),
            agentEvent => Assert.Same(finished, agentEvent));
    }
}

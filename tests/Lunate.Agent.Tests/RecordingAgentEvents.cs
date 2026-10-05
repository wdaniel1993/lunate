namespace Lunate.Agent.Tests;

internal sealed class RecordingAgentEvents : IAgentEvents
{
    private readonly List<AgentEvent> _events = [];

    public IReadOnlyList<AgentEvent> Events => _events;

    public void Emit(AgentEvent agentEvent) => _events.Add(agentEvent);
}

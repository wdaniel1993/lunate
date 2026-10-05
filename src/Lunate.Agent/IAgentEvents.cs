namespace Lunate.Agent;

/// <summary>Receives the events of a run; the loop and tools emit through this.</summary>
public interface IAgentEvents
{
    void Emit(AgentEvent agentEvent);
}

using Lunate.Agent;

namespace Lunate.Roslyn.Tests;

internal sealed class NoopAgentEvents : IAgentEvents
{
    public void Emit(AgentEvent agentEvent) { }
}

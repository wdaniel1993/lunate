using Lunate.Contracts;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

internal sealed class DecisionServiceAdapter(IAgentDecisionService agent) : IDecisionService
{
    public Task<DecisionResponse> DecideAsync(
        DecisionRequest request,
        CancellationToken cancellationToken = default
    ) => agent.DecideAsync(request, cancellationToken);
}

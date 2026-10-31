using Lunate.Contracts;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

internal sealed class AgentDecisionService(IChatClient client, string modelId, string method)
    : IAgentDecisionService
{
    public Task<DecisionResponse> DecideAsync(
        DecisionRequest request,
        CancellationToken cancellationToken = default
    ) => DecisionEngine.EvaluateAsync(request, client, modelId, method, cancellationToken);
}

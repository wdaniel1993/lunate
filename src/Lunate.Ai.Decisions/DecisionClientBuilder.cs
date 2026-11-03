using Microsoft.Extensions.AI;

namespace Lunate.Ai.Decisions;

internal sealed class DecisionClientBuilder
{
    private IDecisionClient? _client;

    public DecisionClientBuilder UseClient(IDecisionClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        return this;
    }

    public DecisionClientBuilder UseChatClient(IChatClient client)
    {
        _client = new ChatDecisionClient(client);
        return this;
    }

    public DecisionClientBuilder UseOpenTelemetry()
    {
        _client = new OpenTelemetryDecisionClient(RequireClient());
        return this;
    }

    public IDecisionClient Build() => RequireClient();

    private IDecisionClient RequireClient() =>
        _client
        ?? throw new InvalidOperationException("Configure a decision client before building.");
}

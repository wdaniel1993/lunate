using Lunate.Ai.Decisions;
using Microsoft.Extensions.AI;

namespace Lunate.Ai.SystemOne;

internal sealed class SystemOneChatFacade(string modelId, IDecisionClient decisionClient) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => throw ChatNotAllowed();

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => throw ChatNotAllowed();

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(IDecisionClient) ? decisionClient : null;

    public void Dispose() { }

    private InvalidOperationException ChatNotAllowed() =>
        new($"model {modelId} is a decision model; it cannot chat.");
}

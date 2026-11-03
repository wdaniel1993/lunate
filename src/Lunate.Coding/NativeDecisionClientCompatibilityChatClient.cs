using Lunate.Agent;
using Lunate.Ai.Decisions;
using Lunate.Contracts;
using Microsoft.Extensions.AI;

namespace Lunate.Coding;

/// <summary>Preserves the stable native-decision service for older extension consumers.</summary>
internal sealed class NativeDecisionClientCompatibilityChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly INativeDecisionClient _legacyDecisionClient;

    public NativeDecisionClientCompatibilityChatClient(IChatClient inner, string modelId)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        if (inner.GetService(typeof(IDecisionClient)) is not IDecisionClient decisionClient)
        {
            throw new InvalidOperationException(
                "The decision compatibility wrapper requires an IDecisionClient service."
            );
        }

        _inner = inner;
        _legacyDecisionClient = new NativeDecisionClientForwarder(decisionClient, modelId);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => _inner.GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => _inner.GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(INativeDecisionClient)
            ? _legacyDecisionClient
            : _inner.GetService(serviceType, serviceKey);

    public void Dispose() => _inner.Dispose();
}

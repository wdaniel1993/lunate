using Microsoft.Extensions.AI;

namespace Lunate.Coding;

/// <summary>
/// Creates the real client on the first request instead of at construction, so an interactive
/// session with no usable credential can finish composing and offer the <c>/auth</c> picker before
/// the credential is resolved. The factory call (and its explicit missing-credential error) then
/// happens when a run first asks the model, after the person may have saved a key. Creation is
/// thread-safe and happens exactly once; <see cref="Dispose"/> touches only a created client.
/// </summary>
internal sealed class DeferredChatClient : IChatClient
{
    private readonly Lazy<IChatClient> _client;

    internal DeferredChatClient(Func<IChatClient> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _client = new Lazy<IChatClient>(create, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => _client.Value.GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => _client.Value.GetStreamingResponseAsync(messages, options, cancellationToken);

    /// <summary>
    /// The inner client's service when it was created; null before the first request, so metadata
    /// lookups never force the credential resolution this class exists to postpone.
    /// </summary>
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        _client.IsValueCreated ? _client.Value.GetService(serviceType, serviceKey) : null;

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }
}

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace MemoryProviderExtension.Tests;

/// <summary>
/// A scripted client for tests that do not verify request contents: it answers every call with the
/// next queued reply, and streaming calls end with a stop update.
/// </summary>
internal sealed class TextReplyChatClient(params string[] replies) : IChatClient
{
    private readonly Queue<string> _replies = new(replies);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Next())));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        string reply = Next();
        await Task.CompletedTask;
        yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(reply)]);
        yield return new ChatResponseUpdate(ChatRole.Assistant, [])
        {
            FinishReason = ChatFinishReason.Stop,
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private string Next() =>
        _replies.Count > 0
            ? _replies.Dequeue()
            : throw new InvalidOperationException("TextReplyChatClient has no reply left.");
}

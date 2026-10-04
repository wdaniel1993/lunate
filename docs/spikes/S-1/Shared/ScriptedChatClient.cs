using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Spike.Shared;

public sealed record ScriptedUpdate(ChatResponseUpdate Update, Action? AfterYield = null);

public sealed class ScriptedTurn(IReadOnlyList<ScriptedUpdate> updates)
{
    public IReadOnlyList<ScriptedUpdate> Updates { get; } = updates;
}

public sealed record RecordedRequest(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options);

public sealed class ScriptedChatClient(IEnumerable<ScriptedTurn> turns) : IChatClient
{
    private readonly Queue<ScriptedTurn> _turns = new(turns);

    public List<RecordedRequest> Requests { get; } = [];

    public int RemainingTurns => _turns.Count;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(new RecordedRequest(messages as IReadOnlyList<ChatMessage> ?? messages.ToList(), options));

        if (_turns.Count == 0)
        {
            return Task.FromResult(new ChatResponse());
        }

        ChatResponseUpdate[] updates = _turns.Dequeue().Updates.Select(u => u.Update).ToArray();
        return Task.FromResult(updates.ToChatResponse());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add(new RecordedRequest(messages as IReadOnlyList<ChatMessage> ?? messages.ToList(), options));

        if (_turns.Count == 0)
        {
            yield break;
        }

        foreach (ScriptedUpdate item in _turns.Dequeue().Updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item.Update;
            cancellationToken.ThrowIfCancellationRequested();
            item.AfterYield?.Invoke();
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

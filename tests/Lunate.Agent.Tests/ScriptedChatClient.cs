using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Script> _scripts = new();

    public List<ScriptedRequest> Requests { get; } = [];

    public ScriptedChatClient Enqueue(params ChatResponseUpdate[] updates)
    {
        _scripts.Enqueue(new Script(updates, null));
        return this;
    }

    public ScriptedChatClient EnqueueFailure(Exception failure, params ChatResponseUpdate[] before)
    {
        _scripts.Enqueue(new Script(before, failure));
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        Script script = Dequeue(messages, options);
        return script.Failure is not null
            ? Task.FromException<ChatResponse>(script.Failure)
            : Task.FromResult(script.Updates.ToChatResponse());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        Script script = Dequeue(messages, options);
        foreach (ChatResponseUpdate update in script.Updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }

        if (script.Failure is not null)
        {
            throw script.Failure;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private Script Dequeue(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        Requests.Add(new ScriptedRequest([.. messages], options));
        return _scripts.Count > 0
            ? _scripts.Dequeue()
            : throw new InvalidOperationException(
                "ScriptedChatClient has no scripted response left."
            );
    }

    private sealed record Script(IReadOnlyList<ChatResponseUpdate> Updates, Exception? Failure);
}

internal sealed record ScriptedRequest(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options);

internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default
    )
    {
        var list = new List<T>();
        await foreach (T item in source.WithCancellation(cancellationToken))
        {
            list.Add(item);
        }

        return list;
    }
}

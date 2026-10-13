using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Script> _scripts = new();
    private readonly Dictionary<int, TaskCompletionSource> _started = new();
    private readonly Dictionary<int, TaskCompletionSource> _release = new();

    public List<ScriptedRequest> Requests { get; } = [];

    /// <summary>The start of one call as an awaitable; 1-based over the call order.</summary>
    public Task WaitForCallAsync(int call)
    {
        lock (_started)
        {
            return Started(call).Task;
        }
    }

    /// <summary>Holds the given call until <see cref="Release"/> is called.</summary>
    public void Gate(int call)
    {
        lock (_release)
        {
            _release[call] = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }
    }

    /// <summary>Releases a gated call.</summary>
    public void Release(int call)
    {
        lock (_release)
        {
            if (_release.TryGetValue(call, out TaskCompletionSource? gate))
            {
                gate.TrySetResult();
            }
        }
    }

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

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        (Script script, Task? gate) = Dequeue(messages, options);
        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return script.Failure is not null ? throw script.Failure : script.Updates.ToChatResponse();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        (Script script, Task? gate) = Dequeue(messages, options);
        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

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

    private (Script Script, Task? Gate) Dequeue(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options
    )
    {
        int call;
        lock (_started)
        {
            Requests.Add(new ScriptedRequest([.. messages], options));
            call = Requests.Count;
            Started(call).TrySetResult();
        }

        Task? gate;
        lock (_release)
        {
            gate = _release.TryGetValue(call, out TaskCompletionSource? pending)
                ? pending.Task
                : null;
        }

        Script script =
            _scripts.Count > 0
                ? _scripts.Dequeue()
                : throw new InvalidOperationException(
                    "ScriptedChatClient has no scripted response left."
                );
        return (script, gate);
    }

    private TaskCompletionSource Started(int call)
    {
        if (!_started.TryGetValue(call, out TaskCompletionSource? started))
        {
            _started[call] = started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        return started;
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

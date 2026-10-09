using System.Runtime.CompilerServices;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>A scripted provider for print-mode tests: one queued update list per model call.</summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<ChatResponseUpdate[]> _scripts = new();

    public ScriptedChatClient Enqueue(params ChatResponseUpdate[] updates)
    {
        _scripts.Enqueue(updates);
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Dequeue().ToChatResponse());

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ChatResponseUpdate[] updates = Dequeue();
        foreach (ChatResponseUpdate update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }

        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private ChatResponseUpdate[] Dequeue() =>
        _scripts.Count > 0
            ? _scripts.Dequeue()
            : throw new InvalidOperationException(
                "ScriptedChatClient has no scripted response left."
            );
}

/// <summary>A client that always asks for one missing-file read; reaches the step limit.</summary>
internal sealed class LoopingChatClient : IChatClient
{
    private int _calls;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Next().ToChatResponse());

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        yield return Scripts.Call(
            $"call-{Interlocked.Increment(ref _calls)}",
            "read",
            Scripts.Args(("path", "missing.txt"))
        );
        yield return Scripts.ToolCalls();
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private ChatResponseUpdate[] Next() =>
        [
            Scripts.Call(
                $"call-{Interlocked.Increment(ref _calls)}",
                "read",
                Scripts.Args(("path", "missing.txt"))
            ),
            Scripts.ToolCalls(),
        ];
}

/// <summary>A client that cancels the run as the model stream starts.</summary>
internal sealed class CancellingChatClient(CancellationTokenSource cancellation) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellation.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ChatResponse());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        cancellation.Cancel();
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        yield return Scripts.Stop();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>The factory seam: returns the scripted client and records the resolved model.</summary>
internal sealed class FakeChatClientFactory(IChatClient client) : IChatClientFactory
{
    public ModelInfo? CreatedModel { get; private set; }

    public IChatClient Create(ModelInfo model)
    {
        CreatedModel = model;
        return client;
    }
}

/// <summary>Builders for scripted provider updates.</summary>
internal static class Scripts
{
    internal static ChatResponseUpdate Text(string text) =>
        new(ChatRole.Assistant, [new TextContent(text)]);

    internal static ChatResponseUpdate Call(
        string callId,
        string name,
        IDictionary<string, object?> arguments
    ) => new(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]);

    internal static ChatResponseUpdate ToolCalls() =>
        new(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.ToolCalls };

    internal static ChatResponseUpdate Stop() =>
        new(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.Stop };

    internal static Dictionary<string, object?> Args(params (string Key, object? Value)[] entries)
    {
        var arguments = new Dictionary<string, object?>();
        foreach ((string key, object? value) in entries)
        {
            arguments[key] = value;
        }

        return arguments;
    }
}

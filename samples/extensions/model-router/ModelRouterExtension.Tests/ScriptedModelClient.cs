using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ModelRouterExtension.Tests;

/// <summary>A local scripted model client that records every request and replays queued turns.</summary>
internal sealed class ScriptedModelClient : IChatClient
{
    private readonly Queue<ChatResponseUpdate[]> _scripts = new();

    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    /// <summary>Queues one turn; several groups concatenate into one model response.</summary>
    public ScriptedModelClient Enqueue(params ChatResponseUpdate[][] groups)
    {
        _scripts.Enqueue([.. groups.SelectMany(group => group)]);
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new ChatResponse());

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ChatResponseUpdate[] script;
        lock (Requests)
        {
            Requests.Add([.. messages]);
            script =
                _scripts.Count > 0
                    ? _scripts.Dequeue()
                    : throw new InvalidOperationException(
                        "ScriptedModelClient has no scripted response left."
                    );
        }

        foreach (ChatResponseUpdate update in script)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>The scripted model turns the tests enqueue.</summary>
internal static class ModelScripts
{
    public static ChatResponseUpdate[] Call(
        string callId,
        string toolName,
        Dictionary<string, object?> arguments
    ) =>
        [
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [new FunctionCallContent(callId, toolName, arguments)]
            ),
            new ChatResponseUpdate(ChatRole.Assistant, [])
            {
                FinishReason = ChatFinishReason.ToolCalls,
            },
        ];

    public static ChatResponseUpdate[] Text(string text) =>
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(text)])
            {
                FinishReason = ChatFinishReason.Stop,
            },
        ];

    public static Dictionary<string, object?> Args(params (string Key, object? Value)[] entries)
    {
        var arguments = new Dictionary<string, object?>();
        foreach ((string key, object? value) in entries)
        {
            arguments[key] = value;
        }

        return arguments;
    }
}

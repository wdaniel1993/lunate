using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace McpFromExtensionExtension.Tests;

/// <summary>
/// A local scripted model client that records every request. The tool results depend on the real
/// server round trip, so replay fixtures would be circular; this client makes the assertion
/// explicit instead.
/// </summary>
internal sealed class ScriptedModelClient : IChatClient
{
    private readonly Queue<ChatResponseUpdate[]> _scripts = new();

    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public ScriptedModelClient Enqueue(params ChatResponseUpdate[] updates)
    {
        _scripts.Enqueue(updates);
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ChatResponseUpdate[] script = Dequeue(messages);
        return Task.FromResult(script.ToChatResponse());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ChatResponseUpdate[] script = Dequeue(messages);
        foreach (ChatResponseUpdate update in script)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }

        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private ChatResponseUpdate[] Dequeue(IEnumerable<ChatMessage> messages)
    {
        Requests.Add([.. messages]);
        return _scripts.Count > 0
            ? _scripts.Dequeue()
            : throw new InvalidOperationException(
                "ScriptedModelClient has no scripted response left."
            );
    }
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
}

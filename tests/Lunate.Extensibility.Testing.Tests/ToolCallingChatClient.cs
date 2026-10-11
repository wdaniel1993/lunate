using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Lunate.Extensibility.Testing.Tests;

/// <summary>
/// Requests one tool call on the first model call and answers with text on the second, so a run
/// exercises declaration, approval and execution of the named tool.
/// </summary>
internal sealed class ToolCallingChatClient(
    string toolName,
    IDictionary<string, object?> arguments,
    string finalReply = "done"
) : IChatClient
{
    private int _calls;

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await Task.CompletedTask;
        if (Interlocked.Increment(ref _calls) == 1)
        {
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [new FunctionCallContent("call-1", toolName, arguments)]
            );
            yield return new ChatResponseUpdate(ChatRole.Assistant, [])
            {
                FinishReason = ChatFinishReason.ToolCalls,
            };
        }
        else
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(finalReply)])
            {
                FinishReason = ChatFinishReason.Stop,
            };
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, finalReply)));

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>Reports the loop's UTF-8 request estimate as provider input usage.</summary>
internal sealed class ContextAccountingChatClient(ScriptedChatClient inner) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ChatMessage[] request = [.. messages];
        List<ChatResponseUpdate> updates = [Usage(request)];
        await foreach (
            ChatResponseUpdate update in inner
                .GetStreamingResponseAsync(request, options, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            updates.Add(update);
        }

        return updates.ToChatResponse();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ChatMessage[] request = [.. messages];
        yield return Usage(request);
        await foreach (
            ChatResponseUpdate update in inner
                .GetStreamingResponseAsync(request, options, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        inner.GetService(serviceType, serviceKey);

    public void Dispose() => inner.Dispose();

    private static ChatResponseUpdate Usage(IReadOnlyList<ChatMessage> request) =>
        new(
            ChatRole.Assistant,
            [
                new UsageContent(
                    new UsageDetails
                    {
                        InputTokenCount = CompactionReducer.Utf8Length(request) / 4,
                        OutputTokenCount = 0,
                    }
                ),
            ]
        );
}

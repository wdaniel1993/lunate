using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Lunate.Ai;

/// <summary>
/// Wire-time turn merge for the Anthropic provider. The Anthropic adapter maps every non-assistant
/// message (tool results included) to the wire <c>user</c> role, so a steering message appended
/// after tool results - or two tool results of one batch - would become consecutive user turns,
/// which the API rejects. This client merges each run of consecutive wire-user messages into its
/// first message: a steering text therefore lands in the turn that carries the tool results.
/// History and session are untouched - the merge is transport, not truth.
/// </summary>
internal sealed class AnthropicTurnMerge : DelegatingChatClient
{
    public AnthropicTurnMerge(IChatClient innerClient)
        : base(innerClient) { }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (
            ChatResponseUpdate update in base.GetStreamingResponseAsync(
                Merge(messages),
                options,
                cancellationToken
            )
        )
        {
            yield return update;
        }
    }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => base.GetResponseAsync(Merge(messages), options, cancellationToken);

    /// <summary>
    /// Merges every run of consecutive messages that the Anthropic adapter maps to the wire user
    /// role (tool results and user messages) into the run's first message, appending the later
    /// contents in order. Assistant and system messages always start a new run.
    /// </summary>
    internal static IReadOnlyList<ChatMessage> Merge(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        List<ChatMessage> merged = [];
        foreach (ChatMessage message in messages)
        {
            if (merged.Count > 0 && IsWireUser(merged[^1]) && IsWireUser(message))
            {
                merged[^1] = AppendContents(merged[^1], message);
            }
            else
            {
                merged.Add(message);
            }
        }

        return merged;
    }

    private static bool IsWireUser(ChatMessage message) =>
        message.Role != ChatRole.Assistant && message.Role != ChatRole.System;

    /// <summary>A copy of the target with the source's contents appended; the originals stay intact.</summary>
    private static ChatMessage AppendContents(ChatMessage target, ChatMessage source)
    {
        List<AIContent> contents = [.. target.Contents, .. source.Contents];
        return new ChatMessage(target.Role, contents)
        {
            AdditionalProperties = target.AdditionalProperties,
            AuthorName = target.AuthorName,
            CreatedAt = target.CreatedAt,
            MessageId = target.MessageId,
            RawRepresentation = target.RawRepresentation,
        };
    }
}

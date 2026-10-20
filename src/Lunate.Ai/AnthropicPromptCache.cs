using System.Runtime.CompilerServices;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace Lunate.Ai;

/// <summary>
/// Anthropic-only prompt-cache breakpoints, applied request-locally: the last system text block, the
/// last tool declaration and the last content block of the latest message. It is a delegating client
/// because the SDK adapter appends its own messages to any raw request, so the complete conversation
/// cannot be rebuilt in the raw-request factory without duplicating it. The client sits below
/// <see cref="AnthropicTurnMerge"/> and above the recorder/provider, so the rolling marker lands on
/// the final merged last block and recordings see the wire request. History, prompt sections and
/// registered tool declarations are never mutated.
/// </summary>
internal sealed class AnthropicPromptCache(IChatClient innerClient)
    : DelegatingChatClient(innerClient)
{
    private const string ContentCacheControlProperty = "anthropic:cache_control";
    private const string ToolCacheControlProperty = "CacheControl";
    private const int MaxBreakpoints = 4;

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        MarkedRequest marked = Mark(messages, options);
        AssertWithinLimit(marked);
        return base.GetResponseAsync(marked.Messages, marked.Options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        MarkedRequest marked = Mark(messages, options);
        AssertWithinLimit(marked);
        await foreach (
            ChatResponseUpdate update in base.GetStreamingResponseAsync(
                marked.Messages,
                marked.Options,
                cancellationToken
            )
        )
        {
            yield return update;
        }
    }

    private static void AssertWithinLimit(MarkedRequest marked)
    {
        int markerCount =
            marked.Messages.Sum(message => message.Contents.Count(IsContentMarked))
            + (marked.Options?.Tools?.Count(IsToolMarked) ?? 0);
        if (markerCount > MaxBreakpoints)
        {
            throw new InvalidOperationException(
                $"Anthropic prompt caching allows at most {MaxBreakpoints} cache_control "
                    + $"breakpoints per request; found {markerCount}. Remove cache_control markers "
                    + "from history or tool declarations before sending."
            );
        }
    }

    private static bool IsContentMarked(AIContent content) =>
        content.AdditionalProperties?.ContainsKey(ContentCacheControlProperty) ?? false;

    private static bool IsToolMarked(AITool tool) =>
        tool.AdditionalProperties.ContainsKey(ToolCacheControlProperty);

    private static MarkedRequest Mark(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        ArgumentNullException.ThrowIfNull(messages);
        List<ChatMessage> source = [.. messages];
        List<ChatMessage> marked = [.. source.Select(static message => message.Clone())];
        MarkSystem(source, marked);
        MarkLatest(source, marked);
        return new MarkedRequest(marked, MarkLastTool(options));
    }

    private static void MarkSystem(IReadOnlyList<ChatMessage> source, List<ChatMessage> marked)
    {
        for (int messageIndex = source.Count - 1; messageIndex >= 0; messageIndex--)
        {
            if (source[messageIndex].Role != ChatRole.System)
            {
                continue;
            }

            IList<AIContent> contents = source[messageIndex].Contents;
            for (int contentIndex = contents.Count - 1; contentIndex >= 0; contentIndex--)
            {
                if (contents[contentIndex] is not TextContent)
                {
                    continue;
                }

                if (CopyWithMarker(contents[contentIndex]) is not { } copy)
                {
                    return;
                }

                marked[messageIndex].Contents = Replace(contents, contentIndex, copy);
                return;
            }

            return;
        }
    }

    private static void MarkLatest(IReadOnlyList<ChatMessage> source, List<ChatMessage> marked)
    {
        if (source.Count == 0 || source[^1].Role == ChatRole.System)
        {
            return;
        }

        IList<AIContent> contents = source[^1].Contents;
        if (contents.Count == 0 || CopyWithMarker(contents[^1]) is not { } copy)
        {
            return;
        }

        marked[^1].Contents = Replace(contents, contents.Count - 1, copy);
    }

    private static ChatOptions? MarkLastTool(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 } tools || tools[^1] is not AIFunction function)
        {
            return options;
        }

        ChatOptions clone = options.Clone();
        List<AITool> clonedTools = [.. tools];
        clonedTools[^1] = new CacheControlledFunction(function);
        clone.Tools = clonedTools;
        return clone;
    }

    private static List<AIContent> Replace(
        IList<AIContent> contents,
        int index,
        AIContent replacement
    )
    {
        List<AIContent> copy = [.. contents];
        copy[index] = replacement;
        return copy;
    }

    /// <summary>
    /// Copies the block and carries over its annotations, raw representation and additional
    /// properties. The marker mutates the content it is applied to, so it is never applied to the
    /// caller's object. Unsupported block types yield no marker rather than being mutated.
    /// </summary>
    private static AIContent? CopyWithMarker(AIContent content)
    {
        if (CopyContent(content) is not { } copy)
        {
            return null;
        }

        copy.WithCacheControl(new CacheControlEphemeral());
        return copy;
    }

    /// <summary>A typed copy of a supported content block; other block types are left alone.</summary>
    internal static AIContent? CopyContent(AIContent content)
    {
        AIContent? copy = content switch
        {
            TextContent text => new TextContent(text.Text),
            TextReasoningContent reasoning => new TextReasoningContent(reasoning.Text)
            {
                ProtectedData = reasoning.ProtectedData,
            },
            FunctionCallContent call => new FunctionCallContent(
                call.CallId,
                call.Name,
                call.Arguments
            )
            {
                Exception = call.Exception,
                InformationalOnly = call.InformationalOnly,
            },
            FunctionResultContent result => new FunctionResultContent(result.CallId, result.Result)
            {
                Exception = result.Exception,
            },
            _ => null,
        };

        if (copy is null)
        {
            return null;
        }

        copy.Annotations = content.Annotations is null ? null : [.. content.Annotations];
        copy.RawRepresentation = content.RawRepresentation;
        copy.AdditionalProperties = content.AdditionalProperties?.Clone();
        return copy;
    }

    private readonly record struct MarkedRequest(
        IReadOnlyList<ChatMessage> Messages,
        ChatOptions? Options
    );

    /// <summary>
    /// The declaration adapter with the cache marker added to its additional properties; the
    /// registered function itself stays untouched.
    /// </summary>
    private sealed class CacheControlledFunction : DelegatingAIFunction
    {
        public CacheControlledFunction(AIFunction inner)
            : base(inner)
        {
            AdditionalProperties = new Dictionary<string, object?>(
                inner.AdditionalProperties,
                StringComparer.Ordinal
            )
            {
                [ToolCacheControlProperty] = new CacheControlEphemeral(),
            };
        }

        public override IReadOnlyDictionary<string, object?> AdditionalProperties { get; }
    }
}

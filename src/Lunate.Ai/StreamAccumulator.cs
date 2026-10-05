using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Ai;

/// <summary>
/// Assembles streamed function-call argument fragments into complete calls so consumers never act on
/// partial calls. A fragment is a <see cref="FunctionCallContent"/> whose <see cref="FunctionCallContent.Arguments"/>
/// dictionary contains exactly one entry under <see cref="ArgumentsFragmentKey"/> holding a raw JSON fragment.
/// Fragments are merged by call id and emitted as one complete call when the stream ends; everything else
/// (including already-complete calls) passes through unchanged.
/// </summary>
internal sealed class StreamAccumulator : DelegatingChatClient
{
    internal const string ArgumentsFragmentKey = "$arguments";

    internal StreamAccumulator(IChatClient innerClient)
        : base(innerClient)
    {
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Dictionary<string, FunctionFragment> fragments = new(StringComparer.Ordinal);
        List<string> order = [];
        string? modelId = null;

        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (update.ModelId is { } id)
            {
                modelId = id;
            }

            bool hasFragments = false;
            foreach (AIContent content in update.Contents)
            {
                if (content is not FunctionCallContent call || !TryGetFragment(call, out string fragment))
                {
                    continue;
                }

                string callId = call.CallId ?? string.Empty;
                if (!fragments.TryGetValue(callId, out FunctionFragment? accumulated))
                {
                    accumulated = new FunctionFragment();
                    fragments[callId] = accumulated;
                    order.Add(callId);
                }

                accumulated.Append(call.Name, fragment);
                hasFragments = true;
            }

            if (!hasFragments)
            {
                yield return update;
                continue;
            }

            List<AIContent> passThrough = [.. update.Contents.Where(
                content => content is not FunctionCallContent call || !TryGetFragment(call, out _))];
            yield return ShallowCopy(update, passThrough);
        }

        if (order.Count > 0)
        {
            List<AIContent> calls = [.. order.Select(callId => fragments[callId].ToCompleteCall(callId))];
            yield return new ChatResponseUpdate(ChatRole.Assistant, calls) { ModelId = modelId };
        }
    }

    private static bool TryGetFragment(FunctionCallContent call, out string fragment)
    {
        fragment = string.Empty;
        if (call.Arguments is not { Count: 1 } arguments ||
            !arguments.TryGetValue(ArgumentsFragmentKey, out object? value))
        {
            return false;
        }

        switch (value)
        {
            case string text:
                fragment = text;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element:
                fragment = element.GetString()!;
                return true;
            default:
                return false;
        }
    }

    private static ChatResponseUpdate ShallowCopy(ChatResponseUpdate update, IList<AIContent> contents) =>
        new(update.Role, contents)
        {
            AuthorName = update.AuthorName,
            MessageId = update.MessageId,
            ResponseId = update.ResponseId,
            ConversationId = update.ConversationId,
            CreatedAt = update.CreatedAt,
            FinishReason = update.FinishReason,
            ModelId = update.ModelId,
            ContinuationToken = update.ContinuationToken,
            AdditionalProperties = update.AdditionalProperties,
            RawRepresentation = update.RawRepresentation,
        };

    private sealed class FunctionFragment
    {
        private readonly StringBuilder _arguments = new();
        private string? _name;

        public void Append(string? name, string fragment)
        {
            if (string.IsNullOrEmpty(_name) && !string.IsNullOrEmpty(name))
            {
                _name = name;
            }

            _ = _arguments.Append(fragment);
        }

        public FunctionCallContent ToCompleteCall(string callId) =>
            FunctionCallContent.CreateFromParsedArguments(
                _arguments.ToString(),
                callId,
                _name ?? string.Empty,
                static json => JsonSerializer.Deserialize<IDictionary<string, object>>(json, AIJsonUtilities.DefaultOptions)!);
    }
}

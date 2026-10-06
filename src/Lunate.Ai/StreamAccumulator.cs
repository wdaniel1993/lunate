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
public sealed class StreamAccumulator(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    /// <summary>The reserved argument key that carries a raw, unassembled JSON fragment.</summary>
    public const string ArgumentsFragmentKey = "$arguments";

    /// <summary>
    /// Whether <paramref name="call"/> must not be executed: its assembly failed, or it still carries
    /// the reserved fragment key. Deliberately looser than the accumulator's merge rule - any value,
    /// extra keys included - because a call that still holds the reserved key is never complete.
    /// </summary>
    public static bool IsUnassembled(FunctionCallContent call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call.Exception is not null
            || (call.Arguments?.ContainsKey(ArgumentsFragmentKey) ?? false);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, FunctionFragment> fragments = new(StringComparer.Ordinal);
        List<string> order = [];
        string? modelId = null;

        await foreach (
            ChatResponseUpdate update in base.GetStreamingResponseAsync(
                messages,
                options,
                cancellationToken
            )
        )
        {
            if (update.ModelId is { } id)
            {
                modelId = id;
            }

            bool hasFragments = false;
            foreach (AIContent content in update.Contents)
            {
                if (content is not FunctionCallContent call || FragmentOf(call) is not { } fragment)
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

            List<AIContent> passThrough =
            [
                .. update.Contents.Where(content =>
                    content is not FunctionCallContent call || FragmentOf(call) is null
                ),
            ];
            yield return ShallowCopy(update, passThrough);
        }

        if (order.Count > 0)
        {
            List<AIContent> calls =
            [
                .. order.Select(callId => fragments[callId].ToCompleteCall(callId)),
            ];
            yield return new ChatResponseUpdate(ChatRole.Assistant, calls) { ModelId = modelId };
        }
    }

    /// <summary>
    /// The accumulator's merge rule: only a call whose Arguments dictionary holds exactly one entry
    /// under the reserved key is a pure wire fragment to merge. This is deliberately narrower than
    /// <see cref="IsUnassembled"/>, which flags any call still carrying the reserved key so the
    /// harness never executes it.
    /// </summary>
    private static string? FragmentOf(FunctionCallContent call)
    {
        if (
            call.Arguments is not { Count: 1 } arguments
            || !arguments.TryGetValue(ArgumentsFragmentKey, out object? value)
        )
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };
    }

    private static ChatResponseUpdate ShallowCopy(
        ChatResponseUpdate update,
        IList<AIContent> contents
    ) =>
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

        public FunctionCallContent ToCompleteCall(string callId)
        {
            string raw = _arguments.ToString();
            FunctionCallContent call = FunctionCallContent.CreateFromParsedArguments(
                raw,
                callId,
                _name ?? string.Empty,
                static json =>
                    JsonSerializer.Deserialize<IDictionary<string, object>>(
                        json,
                        AIJsonUtilities.DefaultOptions
                    )!
            );
            if (call.Exception is not null && raw.Length > 0)
            {
                call.Arguments = new Dictionary<string, object?> { [ArgumentsFragmentKey] = raw };
            }

            return call;
        }
    }
}

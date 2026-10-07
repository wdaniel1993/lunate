using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// The compaction strategy, shaped as an <see cref="IChatReducer"/> the loop invokes before a model
/// request. It keeps every system message and the last N user turns (with their complete tool
/// call/result pairs) verbatim and replaces everything older with one summary produced by the same
/// model. MEAI's <c>SummarizingChatReducer</c>/<c>MessageCountingChatReducer</c> are
/// <c>[Experimental]</c> in the pinned 10.10.x and do not honor our rules (all system messages kept,
/// pair-safe boundary, compaction entry), so the reduction lives here behind the same shape.
/// </summary>
internal sealed class CompactionReducer : IChatReducer
{
    internal const int DefaultKeepTurns = 4;
    internal const int DefaultContextWindow = 128_000;
    internal const int TriggerPercent = 80;

    internal const string SummarizationPrompt = """
        You compact a coding agent's conversation so work can continue in a fresh context.
        Produce a concise plain-text handover summary covering:
        - Goal: what the user is trying to achieve.
        - Decisions: choices made and why.
        - Files: files read or changed, and what was done.
        - Open problems: unresolved questions, failing checks and next steps.
        Keep concrete identifiers (paths, commands, error messages). Do not invent facts.
        Do not offer commentary. Incorporate any earlier summary already present.
        """;

    private readonly IChatClient _client;
    private readonly int _keepTurns;

    public CompactionReducer(IChatClient client, int keepTurns)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _keepTurns = keepTurns;
    }

    public async Task<IEnumerable<ChatMessage>> ReduceAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(messages);
        List<ChatMessage> history = messages as List<ChatMessage> ?? [.. messages];
        CompactionReduction reduction = await ReduceWithDetailsAsync(
                history,
                currentSummary: null,
                providedSummary: null,
                cancellationToken
            )
            .ConfigureAwait(false);
        return reduction.SummaryMessage is null
            ? history
            : Rebuild(history, reduction.TailStart, reduction.SummaryMessage);
    }

    /// <summary>
    /// Plans and performs the reduction: resolves the pair-safe tail boundary, summarizes everything
    /// older (or uses <paramref name="providedSummary"/>), and reports failure instead of throwing.
    /// </summary>
    internal async Task<CompactionReduction> ReduceWithDetailsAsync(
        IReadOnlyList<ChatMessage> history,
        ChatMessage? currentSummary,
        string? providedSummary,
        CancellationToken cancellationToken
    )
    {
        int tailStart = FindTailStart(history, _keepTurns);
        List<ChatMessage> part = SummarizablePart(history, tailStart, currentSummary);
        if (part.Count == 0)
        {
            return new CompactionReduction(tailStart, null, null, null);
        }

        if (providedSummary is not null)
        {
            return new CompactionReduction(
                tailStart,
                new ChatMessage(ChatRole.Assistant, providedSummary),
                null,
                null
            );
        }

        try
        {
            ChatResponse response = await _client
                .GetResponseAsync(
                    BuildSummarizationRequest(part),
                    new ChatOptions(),
                    cancellationToken
                )
                .ConfigureAwait(false);
            string? summary = response.Text;
            if (string.IsNullOrWhiteSpace(summary))
            {
                return new CompactionReduction(
                    tailStart,
                    null,
                    "the model returned an empty summary",
                    null
                );
            }

            return new CompactionReduction(
                tailStart,
                new ChatMessage(ChatRole.Assistant, summary),
                null,
                response.Usage
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new CompactionReduction(tailStart, null, exception.Message, null);
        }
    }

    /// <summary>
    /// The index at which the kept tail starts: the N-th user turn counted from the end, expanded
    /// backward while the message before it relates to a tool call, so a call and its result are
    /// never split across the boundary. Zero when there are fewer than N turns.
    /// </summary>
    internal static int FindTailStart(IReadOnlyList<ChatMessage> history, int keepTurns)
    {
        ArgumentNullException.ThrowIfNull(history);
        int turns = 0;
        int start = 0;
        for (int index = history.Count - 1; index >= 0; index--)
        {
            if (history[index].Role != ChatRole.User)
            {
                continue;
            }

            turns++;
            if (turns == keepTurns)
            {
                start = index;
                break;
            }
        }

        if (turns < keepTurns)
        {
            return 0;
        }

        while (start > 0 && IsToolRelated(history[start - 1]))
        {
            start--;
        }

        return start;
    }

    /// <summary>The non-system messages before the tail that the summary covers.</summary>
    internal static List<ChatMessage> SummarizablePart(
        IReadOnlyList<ChatMessage> history,
        int tailStart,
        ChatMessage? currentSummary
    )
    {
        List<ChatMessage> part = [];
        for (int index = 0; index < tailStart; index++)
        {
            if (history[index].Role != ChatRole.System)
            {
                part.Add(history[index]);
            }
        }

        return
            currentSummary is not null
            && part.Count == 1
            && ReferenceEquals(part[0], currentSummary)
            ? []
            : part;
    }

    /// <summary>Rebuilds the history as the kept system messages, the summary and the kept tail.</summary>
    internal static List<ChatMessage> Rebuild(
        IReadOnlyList<ChatMessage> history,
        int tailStart,
        ChatMessage summary
    )
    {
        List<ChatMessage> rebuilt = [];
        for (int index = 0; index < tailStart; index++)
        {
            if (history[index].Role == ChatRole.System)
            {
                rebuilt.Add(history[index]);
            }
        }

        rebuilt.Add(summary);
        for (int index = tailStart; index < history.Count; index++)
        {
            rebuilt.Add(history[index]);
        }

        return rebuilt;
    }

    /// <summary>The one summarization request: the fixed prompt plus the rendered transcript.</summary>
    internal static List<ChatMessage> BuildSummarizationRequest(IReadOnlyList<ChatMessage> part) =>
        [
            new ChatMessage(ChatRole.System, SummarizationPrompt),
            new ChatMessage(ChatRole.User, RenderTranscript(part)),
        ];

    internal static long Utf8Length(IReadOnlyList<ChatMessage> messages)
    {
        long bytes = 0;
        foreach (ChatMessage message in messages)
        {
            bytes += Utf8Length(message);
        }

        return bytes;
    }

    internal static long Utf8Length(ChatMessage message) =>
        Encoding.UTF8.GetByteCount(message.Role.Value)
        + Encoding.UTF8.GetByteCount(RenderContents(message))
        + 3;

    private static bool IsToolRelated(ChatMessage message) =>
        message.Contents.Any(content => content is FunctionCallContent or FunctionResultContent);

    /// <summary>Rendering never fails: malformed or cyclic arguments must not break the estimate.</summary>
    private static string TrySerialize(object? value)
    {
        try
        {
            return JsonSerializer.Serialize(value, AIJsonUtilities.DefaultOptions);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return "(unserializable)";
        }
    }

    private static string RenderTranscript(IReadOnlyList<ChatMessage> messages)
    {
        var builder = new StringBuilder();
        foreach (ChatMessage message in messages)
        {
            builder.Append('[').Append(message.Role.Value).Append("]\n");
            builder.Append(RenderContents(message)).Append('\n');
        }

        return builder.ToString();
    }

    private static string RenderContents(ChatMessage message)
    {
        var builder = new StringBuilder();
        foreach (AIContent content in message.Contents)
        {
            switch (content)
            {
                case TextContent { Text.Length: > 0 } text:
                    builder.Append(text.Text);
                    break;
                case FunctionCallContent call:
                    builder
                        .Append("[tool call ")
                        .Append(call.Name)
                        .Append(' ')
                        .Append(TrySerialize(call.Arguments))
                        .Append(']');
                    break;
                case FunctionResultContent result:
                    builder
                        .Append("[tool result ")
                        .Append(result.CallId)
                        .Append(": ")
                        .Append(result.Result as string ?? TrySerialize(result.Result))
                        .Append(']');
                    break;
            }
        }

        return builder.ToString();
    }
}

/// <summary>One planned reduction: where the tail starts and the summary that replaces the rest.</summary>
internal sealed record CompactionReduction(
    int TailStart,
    ChatMessage? SummaryMessage,
    string? Failure,
    UsageDetails? Usage
);

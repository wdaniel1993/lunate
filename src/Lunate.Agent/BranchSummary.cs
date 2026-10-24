using System.Globalization;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>One branch summarization call's result: the text and what the model reported using.</summary>
internal sealed record BranchSummaryResult(string Summary, UsageDetails? Usage);

/// <summary>
/// The branch summary: one model call that turns the branch being left into a short handover, so the
/// destination path carries what the abandoned branch learned. The prompt is fixed and the estimate
/// is the same bytes/4 rule the compaction trigger uses, so the confirmation can show a token figure
/// without a model call.
/// </summary>
internal static class BranchSummary
{
    internal const string Prompt = """
        You summarise one branch of a coding agent's session that the user is leaving.
        Write a short handover for the branch's work so the conversation can continue elsewhere.
        Cover, in plain text: what the user wanted, what was decided and why, which files were read
        or changed, and what is unfinished or failing. Keep concrete identifiers (paths, commands,
        error messages). Do not invent facts and do not offer commentary.
        """;

    /// <summary>The estimated input tokens of a summary call over <paramref name="transcript"/>.</summary>
    internal static int EstimateTokens(IReadOnlyList<ChatMessage> transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        long tokens = CompactionReducer.Utf8Length(transcript) / 4;
        return (int)Math.Min(tokens, int.MaxValue);
    }

    /// <summary>The one request the summary call makes: the fixed prompt and the rendered branch.</summary>
    internal static List<ChatMessage> Request(IReadOnlyList<ChatMessage> transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        return
        [
            new ChatMessage(ChatRole.System, Prompt),
            new ChatMessage(ChatRole.User, CompactionReducer.RenderTranscript(transcript)),
        ];
    }

    /// <summary>The one-line confirmation the switch shows before it spends a model call.</summary>
    internal static string Confirmation(string entryId, int estimateTokens) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"rewind to {entryId} with a branch summary: ~{estimateTokens} input tokens, one model call will be made"
        );
}

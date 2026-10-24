using Microsoft.Extensions.AI;

namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    /// <summary>
    /// Runs the one branch-summarization call on the session's active model: the fixed prompt and
    /// the transcript of the branch being left. Returns null when the model produced nothing usable,
    /// so the caller can leave the session untouched. The run guard is the same one a turn takes, so
    /// a summary can never interleave with a run's tool calls.
    /// </summary>
    internal async Task<BranchSummaryResult?> SummarizeBranchAsync(
        IReadOnlyList<ChatMessage> transcript,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(transcript);
        if (Interlocked.CompareExchange(ref _runActive, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A run is already in progress on this harness; runs are sequential."
            );
        }

        try
        {
            ChatResponse response = await _client
                .GetResponseAsync(BranchSummary.Request(transcript), new ChatOptions(), ct)
                .ConfigureAwait(false);
            string? summary = response.Text;
            return string.IsNullOrWhiteSpace(summary)
                ? null
                : new BranchSummaryResult(summary.Trim(), response.Usage);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _runActive, 0);
        }
    }
}

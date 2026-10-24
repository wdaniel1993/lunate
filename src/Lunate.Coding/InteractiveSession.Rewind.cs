using System.Globalization;
using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>The token a summary-carrying rewind needs before it spends the model call.</summary>
    private const string RewindConfirm = "confirm";

    /// <summary>
    /// <c>/rewind &lt;entry&gt; [summary] [confirm]</c>: moves the leaf in place by appending one
    /// <c>activeLeaf</c> entry — the file is never rewritten. With <c>summary</c> the command first
    /// reports the estimated input tokens and states that one model call will be made, and writes
    /// nothing until the same command is run with <c>confirm</c>; then the one call runs and its
    /// summary is written as the first entry on the destination path, with its usage added to the
    /// session's token accounting.
    /// </summary>
    private async Task RewindAsync(string argument, CancellationToken ct)
    {
        if (RefusedWhileBusy())
        {
            return;
        }

        if (_session is not { } session)
        {
            Notify("no session file to rewind");
            return;
        }

        string[] parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            Notify("usage: /rewind <entry> [summary] [confirm] (pick one with /tree)");
            return;
        }

        string entryId = parts[0];
        bool summary = parts.Contains("summary", StringComparer.Ordinal);
        bool confirmed = parts.Contains(RewindConfirm, StringComparer.Ordinal);
        string? from = session.ActiveLeafId;
        if (!summary)
        {
            if (!TryMoveLeaf(session, entryId))
            {
                return;
            }

            _harness = BuildHarness(_model);
            Notify(
                FormattableString.Invariant(
                    $"active leaf moved to {entryId}; the previous branch is still in /tree"
                )
            );
            return;
        }

        List<ChatMessage> transcript = session.ToHistory();
        int estimate = BranchSummary.EstimateTokens(transcript);
        if (!confirmed)
        {
            decimal? inputCost = CostCalculator.Compute(_model, estimate, 0);
            Notify(
                BranchSummary.Confirmation(
                    entryId,
                    estimate,
                    _model.Id,
                    inputCost is decimal cost ? CostCalculator.Format(cost) : null
                )
                    + FormattableString.Invariant(
                        $"; run /rewind {entryId} summary {RewindConfirm} to proceed"
                    )
            );
            return;
        }

        if (_harness is not { } harness)
        {
            Notify("no harness to summarise the branch");
            return;
        }

        BranchSummaryResult? result;
        try
        {
            result = await harness.SummarizeBranchAsync(transcript, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Notify("could not summarise the branch: " + exception.Message, NoticeLevel.Error);
            return;
        }

        if (result is null)
        {
            Notify(
                "the model returned an empty branch summary; the session is unchanged",
                NoticeLevel.Warning
            );
            return;
        }

        if (!TryMoveLeaf(session, entryId))
        {
            return;
        }

        session.AppendBranchSummary(from!, result.Summary);
        if (result.Usage is { } usage)
        {
            _inputTokens += usage.InputTokenCount ?? 0;
            _outputTokens += usage.OutputTokenCount ?? 0;
            UpdateStatus();
        }

        _harness = BuildHarness(_model);
        Notify(
            FormattableString.Invariant(
                $"rewound to {entryId} — branch summary written, {transcript.Count} messages summarised"
            )
        );
    }

    /// <summary>Moves the leaf, refusing an id the file does not contain with the store's message.</summary>
    private bool TryMoveLeaf(Session session, string entryId)
    {
        try
        {
            session.MoveLeaf(entryId);
            return true;
        }
        catch (ArgumentException exception)
        {
            Notify(exception.Message, NoticeLevel.Error);
            return false;
        }
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    private readonly List<string?> _historyEntryIds = [];
    private readonly CompactionReducer _compactor;
    private List<string> _replacedEntryIds = [];
    private ChatMessage? _summaryMessage;
    private long _historyUtf8;
    private int? _contextWindow;
    private bool _contextWindowFallbackReported;
    private int? _lastInputTokens;
    private long _lastRequestUtf8;
    private long _compactionInputTokens;
    private long _compactionOutputTokens;

    /// <summary>The input and output tokens the run's compaction summarization calls used.</summary>
    internal (long Input, long Output) CompactionUsageTotals =>
        (_compactionInputTokens, _compactionOutputTokens);

    /// <summary>
    /// Compacts the history now, regardless of the size threshold; the surface <c>/compact</c>
    /// binds to. Returns false when there is nothing older than the kept tail to summarize.
    /// </summary>
    public async Task<bool> CompactNowAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _runActive, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A run is already in progress on this harness; runs are sequential."
            );
        }

        try
        {
            return await ApplyCompactionAsync(RunIds.Next(), channel: null, InitialSections(), ct)
                .ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _runActive, 0);
        }
    }

    /// <summary>
    /// Evaluates the trigger before a model request. The estimate is UTF-8 bytes / 4 of the composed
    /// request, corrected by the last reported input-token usage when no compaction happened since;
    /// the check is O(1) against the running byte counter. Request-local context added by the
    /// <c>ContextBuilding</c> hook is not part of the estimate.
    /// </summary>
    private async ValueTask CompactBeforeRequestAsync(
        string runId,
        AgentEventChannel channel,
        IReadOnlyList<AgentPromptSection> sections,
        CancellationToken ct
    )
    {
        int window = ResolveContextWindow();
        long current = _historyUtf8 + SystemPromptUtf8(sections);
        long estimate = _lastInputTokens is { } baseline
            ? baseline + Math.Max(0, current - _lastRequestUtf8) / 4
            : current / 4;
        if (estimate * 100 < (long)window * CompactionReducer.TriggerPercent)
        {
            return;
        }

        await ApplyCompactionAsync(runId, channel, sections, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the reduction and records it: writes the session entry, swaps in the rebuilt history,
    /// resets the usage correction and emits <see cref="CompactionApplied"/>. Failures leave the
    /// request unchanged and are reported; the threshold stays exceeded so the next request retries.
    /// </summary>
    private async ValueTask<bool> ApplyCompactionAsync(
        string runId,
        AgentEventChannel? channel,
        IReadOnlyList<AgentPromptSection> sections,
        CancellationToken ct
    )
    {
        int boundary = CompactionReducer.FindTailStart(_history, _options.CompactionKeepTurns);
        List<ChatMessage> part = CompactionReducer.SummarizablePart(
            _history,
            boundary,
            _summaryMessage
        );
        if (part.Count == 0)
        {
            return false;
        }

        string? provided = await ProvidedSummaryAsync(runId, part, ct).ConfigureAwait(false);
        CompactionReduction reduction = await _compactor
            .ReduceWithDetailsAsync(_history, _summaryMessage, provided, ct)
            .ConfigureAwait(false);
        if (reduction.SummaryMessage is not { } summaryMessage)
        {
            if (reduction.Failure is { } failure)
            {
                ReportCompactionFailure(runId, failure);
            }

            return false;
        }

        if (reduction.Usage is { } usage)
        {
            _compactionInputTokens += usage.InputTokenCount ?? 0;
            _compactionOutputTokens += usage.OutputTokenCount ?? 0;
            Trace.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"lunate: compaction summarization used {usage.InputTokenCount ?? 0} input and {usage.OutputTokenCount ?? 0} output tokens; not emitted as UsageUpdated."
                )
            );
        }

        int tailStart = reduction.TailStart;
        List<string> replaces =
        [
            .. _replacedEntryIds,
            .. _historyEntryIds.Take(tailStart).OfType<string>(),
        ];
        _options.Session?.AppendCompaction(summaryMessage.Text, replaces);

        List<ChatMessage> rebuilt = CompactionReducer.Rebuild(_history, tailStart, summaryMessage);
        List<string?> rebuiltIds = [];
        for (int index = 0; index < tailStart; index++)
        {
            if (_history[index].Role == ChatRole.System)
            {
                rebuiltIds.Add(_historyEntryIds[index]);
            }
        }

        rebuiltIds.Add(null);
        rebuiltIds.AddRange(_historyEntryIds.Skip(tailStart));

        _history.Clear();
        _history.AddRange(rebuilt);
        _historyEntryIds.Clear();
        _historyEntryIds.AddRange(rebuiltIds);
        _historyUtf8 = CompactionReducer.Utf8Length(_history);
        _summaryMessage = summaryMessage;
        _replacedEntryIds = replaces;
        _lastInputTokens = null;
        _lastRequestUtf8 = 0;

        if (channel is not null)
        {
            long estimatedAfter = (_historyUtf8 + SystemPromptUtf8(sections)) / 4;
            channel.Emit(
                new CompactionApplied(runId, replaces, (int)Math.Min(estimatedAfter, int.MaxValue))
            );
        }

        return true;
    }

    /// <summary>
    /// Invokes the compacting seam before summarization. The provided summary (last non-null wins,
    /// mapped by the adapter) replaces the default; a throwing seam falls back to the default and is
    /// reported. Without hooks this is a no-op.
    /// </summary>
    private async ValueTask<string?> ProvidedSummaryAsync(
        string runId,
        IReadOnlyList<ChatMessage> part,
        CancellationToken ct
    )
    {
        if (_options.Hooks is not { } hooks)
        {
            return null;
        }

        try
        {
            AgentCompactingResult result = await hooks
                .CompactingAsync(
                    new AgentCompactingContext(runId, [.. part.Select(ToContextMessage)]),
                    ct
                )
                .ConfigureAwait(false);
            return
                result is AgentCompactingResult.Provide provide
                && !string.IsNullOrWhiteSpace(provide.Summary)
                ? provide.Summary
                : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.WriteLine(
                $"lunate: compaction hook failed for run '{runId}': {exception.Message} Falling back to the default summarization."
            );
            return null;
        }
    }

    private int ResolveContextWindow()
    {
        if (_contextWindow is { } cached)
        {
            return cached;
        }

        int? window = null;
        if (_options.ModelId is { Length: > 0 } modelId && _options.ModelCatalog is { } catalog)
        {
            window = catalog.Find(modelId)?.ContextWindow;
        }

        if (window is not > 0)
        {
            if (!_contextWindowFallbackReported)
            {
                _contextWindowFallbackReported = true;
                Trace.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"lunate: context window unknown for model '{_options.ModelId ?? "unset"}'; using the default of {CompactionReducer.DefaultContextWindow} tokens."
                    )
                );
            }

            window = CompactionReducer.DefaultContextWindow;
        }

        _contextWindow = window.Value;
        return window.Value;
    }

    private static void ReportCompactionFailure(string runId, string failure) =>
        Trace.WriteLine(
            $"lunate: compaction failed for run '{runId}': {failure} The request is sent unchanged."
        );

    private static long SystemPromptUtf8(IReadOnlyList<AgentPromptSection> sections)
    {
        if (sections.Count == 0)
        {
            return 0;
        }

        long bytes = 2L * (sections.Count - 1);
        foreach (AgentPromptSection section in sections)
        {
            bytes += Encoding.UTF8.GetByteCount(section.Text);
        }

        return bytes;
    }
}

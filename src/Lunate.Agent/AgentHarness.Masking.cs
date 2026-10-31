using System.Globalization;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    /// <summary>When a tool result entered the window, and whether the call failed.</summary>
    private sealed record ObservationFacts(int Turn, bool IsError);

    private readonly Dictionary<string, ObservationFacts> _observationFacts = new(
        StringComparer.Ordinal
    );
    private readonly HashSet<string> _maskedCallIds = [];
    private readonly Dictionary<string, string> _spillPathByCallId = new(StringComparer.Ordinal);
    private long _spillSavedTokens;
    private long _maskSavedTokens;
    private long _readStubSavedTokens;

    /// <summary>
    /// The cumulative context savings by mechanism (spills, masking, read stubs) that this
    /// harness's projections have made so far; <c>/context</c> reports it as three numbers.
    /// </summary>
    public ContextSavings ContextSavings =>
        new(
            Interlocked.Read(ref _spillSavedTokens),
            Interlocked.Read(ref _maskSavedTokens),
            Interlocked.Read(ref _readStubSavedTokens)
        );

    /// <summary>Records the admitted tool result's turn and error status for the mask windows.</summary>
    private void RecordObservation(string callId, bool isError)
    {
        if (callId.Length == 0)
        {
            return;
        }

        _observationFacts[callId] = new ObservationFacts(_projectionTurn, isError);
    }

    /// <summary>Remembers the spill file a result's pointer names, for masked stubs to carry.</summary>
    private void RecordSpillPath(string callId, string path)
    {
        if (callId.Length == 0)
        {
            return;
        }

        _spillPathByCallId[callId] = path;
    }

    /// <summary>Adds the tokens a spill's pointer saved over admitting the full output.</summary>
    private void RecordSpillSaving(string fullOutput, string admitted)
    {
        long saved = ToolOutput.EstimateTokens(fullOutput) - ToolOutput.EstimateTokens(admitted);
        if (saved > 0)
        {
            Interlocked.Add(ref _spillSavedTokens, saved);
        }
    }

    /// <summary>Seeds the mask facts of a resumed history: every stored result is turn 0.</summary>
    private void SeedMaskingMetadata(IReadOnlyList<ChatMessage> history)
    {
        foreach (
            FunctionResultContent result in history
                .Where(message => message.Role == ChatRole.Tool)
                .SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>()
        )
        {
            if (string.IsNullOrEmpty(result.CallId))
            {
                continue;
            }

            _observationFacts[result.CallId] = new ObservationFacts(Turn: 0, IsError: false);
            if (SpillPathFromMarker(result.Result?.ToString() ?? string.Empty) is { } path)
            {
                _spillPathByCallId[result.CallId] = path;
            }
        }
    }

    private void MaskObservations()
    {
        for (int index = 0; index < _history.Count; index++)
        {
            ChatMessage message = _history[index];
            if (message.Role != ChatRole.Tool)
            {
                continue;
            }

            bool changed = false;
            List<AIContent> contents = new(message.Contents.Count);
            foreach (AIContent content in message.Contents)
            {
                if (content is FunctionResultContent result && TryMaskStub(result, out string stub))
                {
                    changed = true;
                    contents.Add(new FunctionResultContent(result.CallId, stub));
                }
                else
                {
                    contents.Add(content);
                }
            }

            if (!changed)
            {
                continue;
            }

            ChatMessage replacement = message.Clone();
            replacement.Contents = [.. contents];
            long removed =
                CompactionReducer.Utf8Length(message) - CompactionReducer.Utf8Length(replacement);
            if (removed > 0)
            {
                Interlocked.Add(ref _maskSavedTokens, removed / 4);
                _historyUtf8 -= removed;
            }

            _history[index] = replacement;
        }
    }

    private bool TryMaskStub(FunctionResultContent result, out string stub)
    {
        stub = string.Empty;
        if (
            string.IsNullOrEmpty(result.CallId)
            || _maskedCallIds.Contains(result.CallId)
            || !_observationFacts.TryGetValue(result.CallId, out ObservationFacts? facts)
            || result.Result?.ToString() is not { } text
            || text.StartsWith("[stale:", StringComparison.Ordinal)
        )
        {
            return false;
        }

        int age = _projectionTurn - facts.Turn;
        int window = facts.IsError
            ? _options.ProjectionMaskErrorTurns
            : _options.ProjectionMaskTurns;
        if (age <= window)
        {
            return false;
        }

        _maskedCallIds.Add(result.CallId);
        stub = MaskStubText(
            age,
            facts.IsError,
            _spillPathByCallId.GetValueOrDefault(result.CallId)
        );
        return true;
    }

    private static string MaskStubText(int age, bool isError, string? spillPath)
    {
        string reported = isError ? "error result" : "result";
        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"[masked: {reported} is {age} turns old; "
        );
        return spillPath is not { Length: > 0 } path
            ? head + "re-run the tool if you need it again]"
            : head + $"full output at {path}]";
    }

    private static string? SpillPathFromMarker(string text)
    {
        const string marker = "full output at ";
        int start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        int end = text.IndexOf(']', start);
        return end > start ? text[start..end] : null;
    }
}

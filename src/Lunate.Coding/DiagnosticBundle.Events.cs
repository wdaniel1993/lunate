using System.Globalization;
using System.Text;
using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding;

internal static partial class DiagnosticBundle
{
    private static void AppendEvents(StringBuilder text, DiagnosticBundleInput input)
    {
        Section(text, "recent events");
        if (input.Events.Count == 0)
        {
            text.Line("(none recorded)");
            return;
        }

        DiagnosticEvent[] listed =
            input.Events.Count <= EventLimit
                ? [.. input.Events]
                : [.. input.Events.Skip(input.Events.Count - EventLimit)];

        text.Line(
            "last "
                + listed.Length.ToString(CultureInfo.InvariantCulture)
                + " of "
                + input.RecordedEvents.ToString(CultureInfo.InvariantCulture)
                + " recorded (oldest first); t+ is from the first listed event, Δ is from the line above"
        );
        text.Line("  #    t+ms   Δms    event");
        DateTimeOffset first = listed[0].At;
        DateTimeOffset previous = first;
        for (var index = 0; index < listed.Length; index++)
        {
            DiagnosticEvent entry = listed[index];
            long sinceFirst = Milliseconds(entry.At - first);
            long sincePrevious = index == 0 ? 0 : Milliseconds(entry.At - previous);
            previous = entry.At;
            text.Line(
                "  "
                    + index.ToString(CultureInfo.InvariantCulture).PadRight(4)
                    + " "
                    + sinceFirst.ToString(CultureInfo.InvariantCulture).PadRight(6)
                    + " "
                    + sincePrevious.ToString(CultureInfo.InvariantCulture).PadRight(6)
                    + " "
                    + entry.Text
            );
        }
    }

    /// <summary>
    /// One event's one-line summary. Text, reasoning, command arguments and tool output are
    /// measured, never copied: the bundle is about timing and shape, not content.
    /// </summary>
    internal static string Describe(AgentEvent agentEvent)
    {
        ArgumentNullException.ThrowIfNull(agentEvent);
        string detail = agentEvent switch
        {
            RunStarted => string.Empty,
            RunFinished finished => " stop=" + finished.StopReason,
            RunError error => " message=" + Truncate(error.Message),
            TextMessageStart start => " msg=" + Short(start.MessageId),
            TextMessageContent content => Count(" msg=" + Short(content.MessageId), content.Text),
            TextMessageEnd end => " msg=" + Short(end.MessageId),
            ReasoningStart start => " msg=" + Short(start.MessageId),
            ReasoningMessageContent content => Count(
                " msg=" + Short(content.MessageId),
                content.Text
            ),
            ReasoningEnd end => " msg=" + Short(end.MessageId),
            ToolCallStart start => " tool=" + start.ToolName + " call=" + Short(start.CallId),
            ToolCallArgs args => Count(" call=" + Short(args.CallId) + " args", args.Args),
            ToolCallEnd end => " call=" + Short(end.CallId),
            ToolCallResult result => Result(result),
            ApprovalRequested requested => " tool="
                + requested.ToolName
                + " call="
                + Short(requested.CallId),
            UsageUpdated usage => Usage(usage),
            ModelRouted routed => " requested="
                + routed.RequestedModel
                + " resolved="
                + routed.ResolvedModel,
            Retrying retrying => " attempt="
                + retrying.Attempt.ToString(CultureInfo.InvariantCulture)
                + " reason="
                + Truncate(retrying.Reason),
            CompactionApplied compaction => " replaced="
                + compaction.ReplacedEntryIds.Count.ToString(CultureInfo.InvariantCulture)
                + " after="
                + compaction.EstimatedTokensAfter.ToString(CultureInfo.InvariantCulture),
            StepLimitReached limit => " max steps="
                + limit.MaxSteps.ToString(CultureInfo.InvariantCulture),
            SteeringInjected injected => injected.Text is { } text
                ? Count(" entry=" + (injected.EntryId ?? "(none)"), text)
                : " entry=" + (injected.EntryId ?? "(none)"),
            ToolProgressUpdate progress => Count(
                " call=" + Short(progress.CallId),
                progress.Message
            ),
            ToolOutputSpillFailed failed => " message=" + Truncate(failed.Message),
            _ => string.Empty,
        };

        string parent = agentEvent.ParentRunId is { } parentId
            ? " parent=" + Short(parentId)
            : string.Empty;
        string source = string.Equals(agentEvent.Source, "core", StringComparison.Ordinal)
            ? string.Empty
            : " source=" + agentEvent.Source;
        return agentEvent.GetType().Name
            + detail
            + " run="
            + Short(agentEvent.RunId)
            + parent
            + source;
    }

    private static string Result(ToolCallResult result) =>
        Count(
            " call="
                + Short(result.CallId)
                + " result="
                + (result.IsError ? "error" : "ok")
                + (result.HasImages ? " images" : string.Empty),
            result.Output
        );

    private static string Usage(UsageUpdated usage)
    {
        UsageDetails details = usage.Usage;
        return " model="
            + (usage.ModelId ?? "(unknown)")
            + " input="
            + CountOrUnknown(details.InputTokenCount)
            + " output="
            + CountOrUnknown(details.OutputTokenCount);
    }

    private static string Count(string head, string text) =>
        FormattableString.Invariant($"{head} chars={text.Length}");

    private static string CountOrUnknown(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "?";

    private static long Milliseconds(TimeSpan elapsed) =>
        Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero));

    /// <summary>A short identifier: the first eight characters, enough to correlate lines.</summary>
    private static string Short(string id) => id.Length <= 8 ? id : id[..8];
}

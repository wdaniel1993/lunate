using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// One event in a run's stream. The set is closed: every concrete event is a sealed record declared
/// here, so the hierarchy stays union-ready (see the guide's S-6 note).
/// </summary>
public abstract record AgentEvent(string RunId)
{
    /// <summary>
    /// The session the event belongs to; stamped centrally by the event channel when the run has a
    /// session attached, so no emitter has to know about sessions.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>The parent run for child runs (subagents later); null for top-level runs.</summary>
    public string? ParentRunId { get; init; }

    /// <summary>The emitter: <c>"core"</c> for built-in events, an extension id for extension events.</summary>
    public string Source { get; init; } = "core";
}

public sealed record RunStarted(string RunId) : AgentEvent(RunId);

public sealed record RunFinished(string RunId, string StopReason) : AgentEvent(RunId);

public sealed record RunError(string RunId, string Message) : AgentEvent(RunId);

public sealed record TextMessageStart(string RunId, string MessageId) : AgentEvent(RunId);

public sealed record TextMessageContent(string RunId, string MessageId, string Text)
    : AgentEvent(RunId);

public sealed record TextMessageEnd(string RunId, string MessageId) : AgentEvent(RunId);

/// <summary>Opens one streamed reasoning span; content events follow until <see cref="ReasoningEnd"/>.</summary>
public sealed record ReasoningStart(string RunId, string MessageId) : AgentEvent(RunId);

public sealed record ReasoningMessageContent(string RunId, string MessageId, string Text)
    : AgentEvent(RunId);

public sealed record ReasoningEnd(string RunId, string MessageId) : AgentEvent(RunId);

public sealed record ToolCallStart(string RunId, string CallId, string ToolName) : AgentEvent(RunId)
{
    /// <summary>The parent call id for nested calls; null for top-level calls.</summary>
    public string? ParentToolCallId { get; init; }
}

public sealed record ToolCallArgs(string RunId, string CallId, string Args) : AgentEvent(RunId)
{
    /// <summary>The parent call id for nested calls; null for top-level calls.</summary>
    public string? ParentToolCallId { get; init; }
}

public sealed record ToolCallEnd(string RunId, string CallId) : AgentEvent(RunId)
{
    /// <summary>The parent call id for nested calls; null for top-level calls.</summary>
    public string? ParentToolCallId { get; init; }
}

public sealed record ToolCallResult(
    string RunId,
    string CallId,
    string Output,
    bool IsError,
    object? Details = null
) : AgentEvent(RunId)
{
    /// <summary>The parent call id for nested calls; null for top-level calls.</summary>
    public string? ParentToolCallId { get; init; }

    /// <summary>Whether the tool produced image parts for this result.</summary>
    public bool HasImages { get; init; }
}

/// <summary>Base for Lunate-specific events; mappers forward or filter them generically.</summary>
public abstract record ExtensionEvent(string RunId) : AgentEvent(RunId);

public sealed record ApprovalRequested(string RunId, string CallId, string ToolName, string Args)
    : ExtensionEvent(RunId)
{
    /// <summary>The asking run's workspace root: where the call will act; null when unknown.</summary>
    public string? WorkspaceRoot { get; init; }
}

public sealed record UsageUpdated(string RunId, UsageDetails Usage) : ExtensionEvent(RunId)
{
    /// <summary>
    /// The resolved model the usage belongs to: the backend for routed calls, the requested model
    /// otherwise; null when the emitter does not know (older streams and test fixtures).
    /// </summary>
    public string? ModelId { get; init; }
}

/// <summary>
/// A call routed through a virtual model: the requested (virtual) model id and the resolved
/// backend id, emitted when the resolution happens.
/// </summary>
public sealed record ModelRouted(string RunId, string RequestedModel, string ResolvedModel)
    : ExtensionEvent(RunId);

public sealed record Retrying(string RunId, int Attempt, string Reason) : ExtensionEvent(RunId);

/// <summary>
/// A compaction that replaced earlier entries with a summary. <see cref="ReplacedEntryIds"/> lists
/// the session entries the summary replaces; <see cref="EstimatedTokensAfter"/> is the estimated
/// size of the rebuilt request in tokens.
/// </summary>
public sealed record CompactionApplied(
    string RunId,
    IReadOnlyList<string> ReplacedEntryIds,
    int EstimatedTokensAfter
) : ExtensionEvent(RunId);

public sealed record StepLimitReached(string RunId, int MaxSteps) : ExtensionEvent(RunId);

/// <summary>
/// A steering message the loop injected into the running history, carrying the appended session
/// entry id (null when no session is attached).
/// </summary>
public sealed record SteeringInjected(string RunId, string? EntryId) : ExtensionEvent(RunId);

/// <summary>Streamed progress a tool reports through its context while it runs.</summary>
public sealed record ToolProgressUpdate(string RunId, string CallId, string Message)
    : ExtensionEvent(RunId);

/// <summary>
/// A best-effort spill write failed; the tool result keeps its count-only marker. Surfaces as a
/// user notice, never as a tool error.
/// </summary>
public sealed record ToolOutputSpillFailed(string RunId, string Message) : ExtensionEvent(RunId);

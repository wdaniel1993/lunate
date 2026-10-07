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
}

/// <summary>Base for Lunate-specific events; mappers forward or filter them generically.</summary>
public abstract record ExtensionEvent(string RunId) : AgentEvent(RunId);

public sealed record ApprovalRequested(string RunId, string CallId, string ToolName, string Args)
    : ExtensionEvent(RunId);

public sealed record UsageUpdated(string RunId, UsageDetails Usage) : ExtensionEvent(RunId);

public sealed record Retrying(string RunId, int Attempt, string Reason) : ExtensionEvent(RunId);

public sealed record CompactionApplied(string RunId) : ExtensionEvent(RunId);

public sealed record StepLimitReached(string RunId, int MaxSteps) : ExtensionEvent(RunId);

/// <summary>Streamed progress a tool reports through its context while it runs.</summary>
public sealed record ToolProgressUpdate(string RunId, string CallId, string Message)
    : ExtensionEvent(RunId);

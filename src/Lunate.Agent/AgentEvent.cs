using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// One event in a run's stream. The set is closed: every concrete event is a sealed record declared
/// here, so the hierarchy stays union-ready (see the guide's S-6 note).
/// </summary>
public abstract record AgentEvent(string RunId);

public sealed record RunStarted(string RunId) : AgentEvent(RunId);

public sealed record RunFinished(string RunId, string StopReason) : AgentEvent(RunId);

public sealed record RunError(string RunId, string Message) : AgentEvent(RunId);

public sealed record TextMessageStart(string RunId, string MessageId) : AgentEvent(RunId);

public sealed record TextMessageContent(string RunId, string MessageId, string Text)
    : AgentEvent(RunId);

public sealed record TextMessageEnd(string RunId, string MessageId) : AgentEvent(RunId);

public sealed record ToolCallStart(string RunId, string CallId, string ToolName)
    : AgentEvent(RunId);

public sealed record ToolCallArgs(string RunId, string CallId, string Args) : AgentEvent(RunId);

public sealed record ToolCallEnd(string RunId, string CallId) : AgentEvent(RunId);

public sealed record ToolCallResult(
    string RunId,
    string CallId,
    string Output,
    bool IsError,
    object? Details = null
) : AgentEvent(RunId);

/// <summary>Base for Lunate-specific events; mappers forward or filter them generically.</summary>
public abstract record ExtensionEvent(string RunId) : AgentEvent(RunId);

public sealed record ApprovalRequested(string RunId, string CallId, string ToolName, string Args)
    : ExtensionEvent(RunId);

public sealed record UsageUpdated(string RunId, UsageDetails Usage) : ExtensionEvent(RunId);

public sealed record Retrying(string RunId, int Attempt, string Reason) : ExtensionEvent(RunId);

public sealed record CompactionApplied(string RunId) : ExtensionEvent(RunId);

public sealed record StepLimitReached(string RunId, int MaxSteps) : ExtensionEvent(RunId);

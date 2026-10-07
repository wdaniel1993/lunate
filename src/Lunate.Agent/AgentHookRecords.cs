using System.Text.Json;

namespace Lunate.Agent;

/// <summary>A named block of the system prompt.</summary>
public sealed record AgentPromptSection(string Name, string Text);

/// <summary>An edit to one prompt section; a null <see cref="Text"/> removes the section.</summary>
public sealed record AgentPromptSectionEdit(string Name, string? Text);

/// <summary>The prompt state before a run's first model call.</summary>
public sealed record AgentRunStartingContext(
    string RunId,
    IReadOnlyList<AgentPromptSection> Sections,
    IReadOnlyList<string> Tools
);

/// <summary>The outcome of the run-starting seam point.</summary>
public abstract record AgentRunStartingResult
{
    private AgentRunStartingResult() { }

    /// <summary>No prompt or tool changes.</summary>
    public sealed record None : AgentRunStartingResult;

    /// <summary>Prompt-section edits and an optional active-tool selection.</summary>
    public sealed record Apply(
        IReadOnlyList<AgentPromptSectionEdit> SectionEdits,
        IReadOnlyList<string>? ActiveTools
    ) : AgentRunStartingResult;
}

/// <summary>A request-local message; text only.</summary>
public sealed record AgentContextMessage(string Role, string Text, string? Source = null);

/// <summary>The request messages before a model call.</summary>
public sealed record AgentContextBuildingContext(
    string RunId,
    IReadOnlyList<AgentContextMessage> Messages
);

/// <summary>The request-local messages the seam adds.</summary>
public sealed record AgentContextBuildingResult(IReadOnlyList<AgentContextMessage> AddedMessages)
{
    /// <summary>No additions.</summary>
    public static AgentContextBuildingResult None { get; } = new([]);
}

/// <summary>The final assistant message of a model call.</summary>
public sealed record AgentMessageCompletedContext(string RunId, string Role, string Text);

/// <summary>The outcome of the message-completed seam point.</summary>
public abstract record AgentMessageCompletedResult
{
    private AgentMessageCompletedResult() { }

    /// <summary>Keep the message as produced.</summary>
    public sealed record Keep : AgentMessageCompletedResult;

    /// <summary>Replace the message text; the same role is persisted.</summary>
    public sealed record Replace(string Text) : AgentMessageCompletedResult;
}

/// <summary>The tool call the model requested, before approval and execution.</summary>
public sealed record AgentToolCallingContext(
    string RunId,
    string CallId,
    string ToolName,
    JsonElement Arguments
);

/// <summary>The outcome of the tool-calling seam point.</summary>
public abstract record AgentToolCallingResult
{
    private AgentToolCallingResult() { }

    /// <summary>Run the call, optionally with mutated arguments.</summary>
    public sealed record Proceed(JsonElement? Arguments) : AgentToolCallingResult;

    /// <summary>Block the call with a reason; nothing is approved or executed.</summary>
    public sealed record Block(string Reason) : AgentToolCallingResult;
}

/// <summary>The result of a tool call, after execution.</summary>
public sealed record AgentToolResultReadyContext(
    string RunId,
    string CallId,
    string ToolName,
    string Output,
    bool IsError
);

/// <summary>The composed tool-result text and attached JSON data.</summary>
public sealed record AgentToolResultReadyResult(string Output, JsonElement? Data);

/// <summary>One extension entry to append to the session at a turn boundary.</summary>
public sealed record AgentExtensionEntry(string ExtensionId, string Type, JsonElement Payload);

/// <summary>An actionable turn boundary.</summary>
public sealed record AgentTurnEndedContext(string RunId);

/// <summary>The entries to append and whether one continuation is requested.</summary>
public sealed record AgentTurnEndedResult(
    IReadOnlyList<AgentExtensionEntry> Entries,
    bool RequestContinuation
)
{
    /// <summary>No entries and no continuation.</summary>
    public static AgentTurnEndedResult None { get; } = new([], false);
}

/// <summary>The settled run; observation only.</summary>
public sealed record AgentRunSettledContext(string RunId);

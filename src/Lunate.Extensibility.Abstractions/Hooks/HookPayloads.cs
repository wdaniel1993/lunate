using System.Text.Json;

namespace Lunate.Extensibility.Abstractions;

/// <summary>The project extension awaiting a trust decision.</summary>
public sealed record ProjectTrustPayload(
    string ExtensionId,
    string ExtensionDirectory,
    string RepositoryIdentity,
    string WorktreePath
);

/// <summary>Payload of the session-lifecycle hooks.</summary>
public sealed record SessionStartedPayload(string WorkingDirectory, string RepositoryIdentity);

/// <summary>Payload of the session-lifecycle hooks.</summary>
public sealed record SessionEndingPayload(string WorkingDirectory, string RepositoryIdentity);

/// <summary>
/// Raw user input, before it reaches the model. Contract-complete but unwired in this change:
/// the input layer/TUI will produce it.
/// </summary>
public sealed record InputReceivedPayload(string Text);

/// <summary>A named, source-tagged block of the system prompt.</summary>
public sealed record PromptSection(string Name, string Text)
{
    /// <summary>The extension that added the section; null for host sections.</summary>
    public string? Source { get; init; }
}

/// <summary>An edit to one prompt section; a null <see cref="Text"/> removes the section.</summary>
public sealed record PromptSectionEdit(string Name, string? Text)
{
    /// <summary>The extension that produced the edit; null for host edits.</summary>
    public string? Source { get; init; }
}

/// <summary>The prompt state before the first model call of a run.</summary>
public sealed record RunStartingPayload(
    string RunId,
    IReadOnlyList<PromptSection> Sections,
    IReadOnlyList<string> Tools
);

/// <summary>A request-local message; text only, so it crosses the JSON boundary.</summary>
public sealed record ContextMessage(string Role, string Text)
{
    /// <summary>The extension that added the message; null for host messages.</summary>
    public string? Source { get; init; }
}

/// <summary>The request messages before a model call.</summary>
public sealed record ContextBuildingPayload(string RunId, IReadOnlyList<ContextMessage> Messages);

/// <summary>One raw provider update; observation only.</summary>
public sealed record ProviderStreamEventPayload(
    string RunId,
    string? ModelId,
    string? Text,
    string? FinishReason
);

/// <summary>The final assistant message of one model call.</summary>
public sealed record MessageCompletedPayload(string RunId, string Role, string Text);

/// <summary>The tool call the model requested, before approval and execution.</summary>
public sealed record ToolCallingPayload(
    string RunId,
    string CallId,
    string ToolName,
    JsonElement Arguments
);

/// <summary>The result of a tool call, after execution.</summary>
public sealed record ToolResultReadyPayload(
    string RunId,
    string CallId,
    string ToolName,
    string Output,
    bool IsError
);

/// <summary>An actionable turn boundary.</summary>
public sealed record TurnEndedPayload(string RunId);

/// <summary>One extension entry to append to the session at a turn boundary.</summary>
public sealed record TurnEndedEntry(string Type, JsonElement Payload)
{
    /// <summary>The extension that produced the entry; stamped by the runner.</summary>
    public string? ExtensionId { get; init; }
}

/// <summary>The end of a run; observation only.</summary>
public sealed record RunSettledPayload(string RunId);

/// <summary>
/// The conversation before compaction. Contract-complete but unwired in this change:
/// the compaction layer will produce it.
/// </summary>
public sealed record CompactingPayload(string RunId, IReadOnlyList<ContextMessage> Messages);

/// <summary>The active model after a change; observation only.</summary>
public sealed record ModelChangedPayload(string RunId, string Model);

/// <summary>The active tool set after a change; observation only.</summary>
public sealed record ToolsChangedPayload(string RunId, IReadOnlyList<string> Tools);

namespace Lunate.Agent;

/// <summary>
/// The outcome of one tool call. <see cref="Output"/> and <see cref="IsError"/> reach the model;
/// <see cref="Details"/> is UI-only (for example a diff) and is never sent.
/// </summary>
public sealed record ToolResult(string Output, bool IsError, object? Details = null);

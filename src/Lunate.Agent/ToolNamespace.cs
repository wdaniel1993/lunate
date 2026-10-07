namespace Lunate.Agent;

/// <summary>
/// The grouping a tool belongs to, for discovery and prompt rendering; MCP servers namespace
/// their tools.
/// </summary>
public sealed record ToolNamespace(
    string Name,
    string? Description = null,
    string? Instructions = null
);

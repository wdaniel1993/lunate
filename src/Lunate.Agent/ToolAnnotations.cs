namespace Lunate.Agent;

/// <summary>
/// MCP-style tool annotations. All default to false; add a hint only when the tool declares it.
/// </summary>
public sealed record ToolAnnotations(
    bool ReadOnly = false,
    bool Destructive = false,
    bool Idempotent = false,
    bool OpenWorld = false
);

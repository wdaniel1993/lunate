namespace Lunate.Protocols;

/// <summary>How to start one MCP server and call its tools.</summary>
public sealed record McpServerOptions(
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? WorkingDirectory = null,
    TimeSpan? CallTimeout = null
);

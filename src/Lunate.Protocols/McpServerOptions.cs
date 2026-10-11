using Lunate.Agent;

namespace Lunate.Protocols;

/// <summary>How to start one MCP server, call its tools and expose them.</summary>
public sealed record McpServerOptions(
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? WorkingDirectory = null,
    TimeSpan? CallTimeout = null,
    ToolExposure Exposure = ToolExposure.Direct,
    IReadOnlyDictionary<string, ToolExposure>? ExposureOverrides = null,
    string? NamespaceName = null
);

using Lunate.Agent;

namespace Lunate.Protocols;

/// <summary>
/// How to start one MCP server, call its tools and expose them. <see cref="Source"/> names where
/// the server came from (the config file path, or an extension attribution) for status displays;
/// it never carries environment values or headers. <see cref="WorkspaceIndependent"/> marks a
/// server whose tools act on no particular working directory, so child runs in other worktrees may
/// be offered them; the default (false) keeps a project server's tools out of different-root
/// children.
/// </summary>
public sealed record McpServerOptions(
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? WorkingDirectory = null,
    TimeSpan? CallTimeout = null,
    ToolExposure Exposure = ToolExposure.Direct,
    IReadOnlyDictionary<string, ToolExposure>? ExposureOverrides = null,
    string? NamespaceName = null,
    string? Source = null,
    bool WorkspaceIndependent = false
);

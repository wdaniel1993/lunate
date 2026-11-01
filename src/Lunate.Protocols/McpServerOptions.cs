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
)
{
    /// <summary>
    /// The credential values Lunate itself knows (the set <c>CredentialRedactorSource.Collect</c>
    /// aggregates) that must be withheld from the child's environment. Null or empty keeps today's
    /// full inheritance; a set makes the host build the child environment explicitly, so a variable
    /// whose value matches one of these is removed entirely — never replaced with an empty string
    /// — unless the server's own <c>env</c> map names the variable (the opt-in, which receives its
    /// value as declared; <c>${VAR}</c> expansion happened when the config loaded).
    /// </summary>
    public IReadOnlyList<string>? WithheldCredentialValues { get; init; }
}

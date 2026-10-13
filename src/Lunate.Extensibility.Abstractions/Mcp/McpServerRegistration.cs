namespace Lunate.Extensibility.Abstractions;

/// <summary>Where a tool is visible: the model, nested execution, both or neither.</summary>
/// <remarks>
/// Mirrors <c>Lunate.Agent.ToolExposure</c> one-to-one; MCP registrations keep this contract enum
/// while tool registrations carry the core enum directly.
/// </remarks>
public enum ToolExposure
{
    /// <summary>Declared to the model and callable from nested execution; the default.</summary>
    Direct,

    /// <summary>Declared to the model only; nested execution refuses it.</summary>
    ModelOnly,

    /// <summary>Callable from nested execution only; never declared to the model.</summary>
    Programmatic,

    /// <summary>Discoverable but not declared upfront (code mode); nested execution refines this later.</summary>
    Deferred,

    /// <summary>Internal; neither declared nor callable programmatically.</summary>
    Hidden,
}

/// <summary>How to start one MCP server: the <c>mcp.json</c> shape, registration-time only.</summary>
public sealed record McpServerConfig(
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? WorkingDirectory = null,
    TimeSpan? CallTimeout = null
);

/// <summary>
/// One MCP server an extension contributes. Registration is registration only: it must not start
/// processes, sockets or timers; the first tool listing starts the process exactly once.
/// </summary>
public sealed record McpServerRegistration(
    string Name,
    McpServerConfig Config,
    ToolExposure Exposure = ToolExposure.Direct,
    IReadOnlyDictionary<string, ToolExposure>? ExposureOverrides = null
);

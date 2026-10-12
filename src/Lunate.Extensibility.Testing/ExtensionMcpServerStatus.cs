namespace Lunate.Extensibility.Testing;

/// <summary>The lifecycle state of one composed extension MCP server.</summary>
public enum ExtensionMcpServerState
{
    /// <summary>The registration was collected; the server has not been started.</summary>
    NotStarted,

    /// <summary>The server started and listed its tools.</summary>
    Started,

    /// <summary>The server failed to start or list, or its process exited after starting.</summary>
    Failed,
}

/// <summary>
/// One composed MCP server's status: its state, listed tool count, failure reason and source
/// (the config file path, or <c>extension '{id}'</c>). Never carries environment values or headers.
/// </summary>
public sealed record ExtensionMcpServerStatus(
    string Name,
    ExtensionMcpServerState State,
    int ToolCount,
    string? Reason,
    string? Source
);

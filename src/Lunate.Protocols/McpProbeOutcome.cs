namespace Lunate.Protocols;

/// <summary>
/// The result of one liveness probe of an MCP server.
/// </summary>
public enum McpProbeOutcome
{
    /// <summary>The server answered inside the probe bound.</summary>
    Alive,

    /// <summary>The call failed with a transport-level error: evidence the process is gone.</summary>
    Exited,

    /// <summary>
    /// The probe bound elapsed without an answer, or the host was never started or was disposed:
    /// no evidence either way, and never treated as an exit.
    /// </summary>
    Unresponsive,
}

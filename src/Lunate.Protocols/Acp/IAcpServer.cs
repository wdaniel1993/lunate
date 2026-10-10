using Lunate.Agent;

namespace Lunate.Protocols.Acp;

/// <summary>
/// The ACP server seam: one connection over an input/output stream pair, a fresh
/// <see cref="AgentHarness"/> per ACP session. LibAcp implements the wire protocol behind it, so a
/// later SDK switch stays cheap.
/// </summary>
/// <remarks>
/// The harness factory receives the ACP session's working directory so the server can build the
/// harness workspace from it (design.md, session/new). The guide's draft signature was
/// parameterless and could not carry the cwd; recorded in the change's design.md (Deviations 1).
/// </remarks>
public interface IAcpServer
{
    /// <summary>
    /// Serves one ACP connection until the peer closes the streams or <paramref name="ct"/>
    /// cancels.
    /// </summary>
    Task RunAsync(
        Stream input,
        Stream output,
        Func<string, AgentHarness> createHarness,
        CancellationToken ct
    );
}

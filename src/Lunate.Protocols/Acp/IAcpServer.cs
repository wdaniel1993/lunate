using Lunate.Agent;

namespace Lunate.Protocols.Acp;

/// <summary>
/// The ACP server seam: one connection over an input/output stream pair, a fresh
/// <see cref="AgentHarness"/> per ACP session. LibAcp implements the wire protocol behind it, so a
/// later SDK switch stays cheap.
/// </summary>
/// <remarks>
/// The harness factory receives the <see cref="AcpSessionContext"/> the adapter built for the
/// session (session/new): the cwd plus the client-backed approver and, when the client offered its
/// file system at initialize, the client file access.
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
        Func<AcpSessionContext, AgentHarness> createHarness,
        CancellationToken ct
    );
}

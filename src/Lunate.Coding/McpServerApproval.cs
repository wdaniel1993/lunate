using Lunate.Protocols;

namespace Lunate.Coding;

/// <summary>
/// The per-server approval prompt: the sibling of <see cref="IMcpConfigTrustPrompt"/> for one
/// server definition from a project file. The question names the server, its source file and the
/// full untruncated command; the decision is recorded per repository identity and definition
/// hash, so a changed definition asks again.
/// </summary>
internal interface IMcpServerApprovalPrompt
{
    ValueTask<bool> ApproveAsync(McpServerOptions server, CancellationToken cancellationToken);
}

/// <summary>
/// The interactive per-server approval: the server name, its source file and the full command
/// (untruncated — it is what is being approved) through the session's live-area question path.
/// </summary>
internal sealed class TuiMcpServerApprovalPrompt(TrustQuestion ask) : IMcpServerApprovalPrompt
{
    public ValueTask<bool> ApproveAsync(
        McpServerOptions server,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(server);
        cancellationToken.ThrowIfCancellationRequested();

        return ask(
            TrustFormat.McpServerQuestion(server),
            [TrustFormat.Command(server)],
            cancellationToken
        );
    }
}

/// <summary>
/// The print- and ACP-mode per-server approval: repository-provided commands are never
/// auto-approved off a terminal. It refuses and the caller logs why, pointing at the interactive
/// path that can approve.
/// </summary>
internal sealed class NonInteractiveMcpServerApprovalPrompt : IMcpServerApprovalPrompt
{
    public ValueTask<bool> ApproveAsync(
        McpServerOptions server,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(server);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}

using Lunate.Agent;

namespace Lunate.Protocols.Acp;

/// <summary>
/// What the ACP adapter knows about one session when it builds the harness: the session's working
/// directory, the client-backed approver (always present in ACP mode) and the client file access
/// when the editor advertised its file system at initialize.
/// </summary>
public sealed class AcpSessionContext
{
    /// <summary>Creates the context for one ACP session.</summary>
    public AcpSessionContext(string cwd, IToolApprover? clientApprover, ITextFileAccess? fileAccess)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cwd);
        Cwd = cwd;
        ClientApprover = clientApprover;
        FileAccess = fileAccess;
    }

    /// <summary>The working directory the client requested for the session.</summary>
    public string Cwd { get; }

    /// <summary>The approver that asks the client for permission; always built by the adapter.</summary>
    public IToolApprover? ClientApprover { get; }

    /// <summary>
    /// The client's file system, when the editor advertised both <c>fs/read_text_file</c> and
    /// <c>fs/write_text_file</c> at initialize; null falls back to the local disk.
    /// </summary>
    public ITextFileAccess? FileAccess { get; }
}

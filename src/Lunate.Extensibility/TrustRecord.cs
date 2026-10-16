namespace Lunate.Extensibility;

/// <summary>Which trust record section a record came from.</summary>
public enum TrustSection
{
    /// <summary>Project-extension trust, keyed per repository and worktree.</summary>
    Extensions,

    /// <summary>Project <c>mcp.json</c> trust, keyed the same way but never shared.</summary>
    McpConfig,

    /// <summary>
    /// Per-server approval, keyed by repository identity and the canonical hash of the server's
    /// raw definition; the record's worktree is where it was approved.
    /// </summary>
    McpServer,
}

/// <summary>
/// One recorded trust decision: the section it lives in, the location it was recorded for — for
/// extension trust the worktree (or a stale worktree path), for a project mcp.json file the config
/// file's path, for a per-server approval the worktree that approved the definition — and the
/// content hash that was approved (an extension directory hash, an mcp.json file hash, or a server
/// definition hash).
/// </summary>
public sealed record TrustRecord(TrustSection Section, string WorktreePath, string ContentHash);

namespace Lunate.Extensibility;

/// <summary>Which trust record section a record came from.</summary>
public enum TrustSection
{
    /// <summary>Project-extension trust, keyed per repository and worktree.</summary>
    Extensions,

    /// <summary>Project <c>mcp.json</c> trust, keyed the same way but never shared.</summary>
    McpConfig,
}

/// <summary>
/// One recorded trust decision: the section it lives in, the worktree it was recorded for and the
/// content hash that was approved (an extension directory hash or an mcp.json file hash).
/// </summary>
public sealed record TrustRecord(TrustSection Section, string WorktreePath, string ContentHash);

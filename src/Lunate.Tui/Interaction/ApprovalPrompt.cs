namespace Lunate.Tui;

/// <summary>The user's decision on a pending tool approval.</summary>
public enum ApprovalChoice
{
    Approve,
    Deny,
    Always,
}

/// <summary>
/// A pending approval as presented to the user: the tool, its argument summary (raw JSON when
/// the wiring layer passes it; <see cref="ToolArgsSummary"/> summarizes it) and the prepared
/// display lines. <see cref="Detail"/>'s first line is the headline action — the command, a
/// <c>path  +N −M</c> change line, a read range or another tool's summary — and the remaining
/// lines are the detail block; null or empty means the renderer falls back to
/// <see cref="ArgsSummary"/>. <see cref="WorkspaceRoot"/> is the display-formatted root the call
/// will act in, set only when it differs from the session's working directory.
/// <see cref="Asker"/> labels the asking run when it is a subagent (for example
/// <c>subagent (depth 1)</c>). The prompt only produces a choice; "always for this session"
/// memory belongs to the wiring layer and applies across the whole run tree.
/// </summary>
public sealed record ApprovalPromptModel(
    string ToolName,
    string ArgsSummary,
    string? Asker = null,
    IReadOnlyList<string>? Detail = null,
    string? WorkspaceRoot = null
)
{
    /// <summary>The detail block's line budget before the remaining lines become a marker.</summary>
    internal const int MaxDetailLines = 6;

    /// <summary>
    /// Decides from a single key: bare <c>y</c>/<c>n</c>/<c>a</c> only. A bare Enter denies and
    /// <c>Esc</c> also denies — nothing runs by accident; every other key returns <c>null</c> (not a
    /// decision).
    /// </summary>
    public ApprovalChoice? Decide(KeyEvent key)
    {
        if (key.Ctrl || key.Alt || key.Shift)
        {
            return null;
        }

        return key switch
        {
            { Kind: KeyKind.Character, Text: "y" } => ApprovalChoice.Approve,
            { Kind: KeyKind.Character, Text: "n" } => ApprovalChoice.Deny,
            { Kind: KeyKind.Character, Text: "a" } => ApprovalChoice.Always,
            { Kind: KeyKind.Enter } => ApprovalChoice.Deny,
            { Kind: KeyKind.Escape } => ApprovalChoice.Deny,
            _ => null,
        };
    }
}

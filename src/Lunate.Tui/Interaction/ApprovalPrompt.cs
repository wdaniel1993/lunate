namespace Lunate.Tui;

/// <summary>The user's decision on a pending tool approval.</summary>
public enum ApprovalChoice
{
    Approve,
    Deny,
    Always,
}

/// <summary>
/// A pending approval as presented to the user: the tool and a one-line argument summary
/// (T-20's <see cref="ToolArgsSummary"/> summarizes raw JSON when the wiring layer passes it).
/// The prompt only produces a choice; "always for this session" memory belongs to the wiring layer.
/// </summary>
public sealed record ApprovalPromptModel(string ToolName, string ArgsSummary)
{
    /// <summary>
    /// Decides from a single key: bare <c>y</c>/<c>n</c>/<c>a</c> only. A bare Enter denies —
    /// nothing runs by accident; Esc and every other key return <c>null</c> (not a decision).
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
            _ => null,
        };
    }
}

namespace Lunate.Tui;

/// <summary>The user's decision on a pending trust question.</summary>
public enum TrustChoice
{
    Approve,
    Deny,
}

/// <summary>
/// A pending trust question as presented to the user: the question title (without its trailing
/// question mark), optional detail lines (one per MCP server) and the position in a series of
/// questions (<c>n of m</c>, shown when several are pending). The prompt only produces a choice;
/// remembering the decision belongs to the wiring layer.
/// </summary>
public sealed record TrustPromptModel(
    string Title,
    IReadOnlyList<string> Details,
    int Step,
    int Total
)
{
    /// <summary>
    /// Decides from a single key: bare <c>y</c>/<c>Y</c> approve; bare <c>n</c>, Enter and Esc deny
    /// (nothing loads by accident); Ctrl+C and every other key return <c>null</c> (not a decision).
    /// </summary>
    public TrustChoice? Decide(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Ctrl || key.Alt || key.Shift)
        {
            return null;
        }

        return key switch
        {
            { Kind: KeyKind.Character, Text: "y" } => TrustChoice.Approve,
            { Kind: KeyKind.Character, Text: "Y" } => TrustChoice.Approve,
            { Kind: KeyKind.Character, Text: "n" } => TrustChoice.Deny,
            { Kind: KeyKind.Enter } => TrustChoice.Deny,
            { Kind: KeyKind.Escape } => TrustChoice.Deny,
            _ => null,
        };
    }
}

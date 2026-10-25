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
/// <para>
/// The selection is prompt state, not a rendering focus: <see cref="Selected"/> is carried by the
/// model, so a repaint — a resize, a redraw, a notice — shows the same choice, and a new prompt
/// starts on <see cref="DefaultSelected"/> (<c>[n]o</c>), which makes Space on an untouched prompt
/// deny exactly like Enter. Moving produces a new model (<see cref="Move"/>), so a repost is a
/// value rather than a mutation.
/// </para>
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

    /// <summary>The three fixed decisions, in display order: <c>[y]es</c>, <c>[n]o</c>, always.</summary>
    public const int ChoiceCount = 3;

    /// <summary>No: a stray Space denies, the same outcome as a stray Enter.</summary>
    public const int DefaultSelected = 1;

    private readonly int _selected = DefaultSelected;

    /// <summary>
    /// The index of the selected choice over <c>[y]es</c>, <c>[n]o</c>, <c>[a]lways this session</c>.
    /// Out-of-range values wrap, so the selection is always one of the three.
    /// </summary>
    public int Selected
    {
        get => _selected;
        init => _selected = Wrap(value);
    }

    /// <summary>Whether the panel also shows the key legend; <c>?</c> sets it, and it decides nothing.</summary>
    public bool ShowLegend { get; init; }

    /// <summary>The choice a move or Space would act on, in display order.</summary>
    public ApprovalChoice Selection => ChoiceAt(_selected);

    /// <summary>Moves the selection by <paramref name="delta"/> choices, wrapping at both ends.</summary>
    public ApprovalPromptModel Move(int delta) => this with { Selected = _selected + delta };

    /// <summary>The decision the selected choice stands for (Space activates it).</summary>
    public ApprovalChoice Activate() => ChoiceAt(_selected);

    /// <summary>True for a bare Up or Down: the only keys that move the selection.</summary>
    public bool Moves(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return !key.Ctrl && !key.Alt && !key.Shift && key.Kind is KeyKind.Up or KeyKind.Down;
    }

    /// <summary>True for a bare <c>?</c>: it shows the panel's key legend and decides nothing.</summary>
    public bool ShowsLegend(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return !key.Ctrl && !key.Alt && !key.Shift && key is { Kind: KeyKind.Character, Text: "?" };
    }

    /// <summary>The same prompt with the key legend shown; the selection and question are unchanged.</summary>
    public ApprovalPromptModel ShowKeyLegend() => this with { ShowLegend = true };

    /// <summary>
    /// Decides from a single key: bare <c>y</c>/<c>n</c>/<c>a</c> decide wherever the selection
    /// sits, Space activates the selected choice, and a bare Enter or <c>Esc</c> denies — nothing
    /// runs by accident. Every other key returns <c>null</c> (not a decision).
    /// </summary>
    public ApprovalChoice? Decide(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Ctrl || key.Alt || key.Shift)
        {
            return null;
        }

        return key switch
        {
            { Kind: KeyKind.Character, Text: "y" } => ApprovalChoice.Approve,
            { Kind: KeyKind.Character, Text: "n" } => ApprovalChoice.Deny,
            { Kind: KeyKind.Character, Text: "a" } => ApprovalChoice.Always,
            { Kind: KeyKind.Space } => Activate(),
            { Kind: KeyKind.Enter } => ApprovalChoice.Deny,
            { Kind: KeyKind.Escape } => ApprovalChoice.Deny,
            _ => null,
        };
    }

    private static ApprovalChoice ChoiceAt(int index) =>
        index switch
        {
            0 => ApprovalChoice.Approve,
            1 => ApprovalChoice.Deny,
            _ => ApprovalChoice.Always,
        };

    private static int Wrap(int index) => ((index % ChoiceCount) + ChoiceCount) % ChoiceCount;
}

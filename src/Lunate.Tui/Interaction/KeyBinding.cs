namespace Lunate.Tui;

/// <summary>
/// The state a key is pressed in. Rows in the catalog apply when every flag they name is present;
/// <see cref="None"/> means the row applies in every state.
/// </summary>
[Flags]
internal enum KeyContext
{
    None = 0,
    Running = 1,
    HasQueue = 2,
    Approval = 4,
    Picker = 8,
    Transcript = 16,
    Bash = 32,
    Composing = 64,
    Secret = 128,
    Trust = 256,
    Idle = 512,
}

/// <summary>
/// A key pattern: the kind, the modifiers it requires and, for characters, the text. A null
/// modifier is not constrained — that is how Escape matches with any modifiers held.
/// </summary>
internal readonly record struct KeyPattern(
    KeyKind Kind,
    bool? Ctrl = null,
    bool? Alt = null,
    bool? Shift = null,
    string? Text = null
)
{
    public bool Matches(KeyEvent key) =>
        key.Kind == Kind
        && (Ctrl is not { } ctrl || key.Ctrl == ctrl)
        && (Alt is not { } alt || key.Alt == alt)
        && (Shift is not { } shift || key.Shift == shift)
        && (Text is null || string.Equals(key.Text, Text, StringComparison.Ordinal));
}

/// <summary>One row of the keymap: what a key means, where it means it, and how to describe it.</summary>
internal sealed record KeyBinding(
    string Id,
    KeyPattern Key,
    RoutedKey Intent,
    KeyContext Contexts,
    string Description
)
{
    public bool AppliesTo(KeyContext context) =>
        Contexts == KeyContext.None || (context & Contexts) == Contexts;
}

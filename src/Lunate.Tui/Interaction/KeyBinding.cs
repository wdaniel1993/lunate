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
    KeyPattern Key,
    RoutedKey Intent,
    KeyContext Contexts,
    string Description
)
{
    public bool AppliesTo(KeyContext context) =>
        Contexts == KeyContext.None || (context & Contexts) == Contexts;
}

/// <summary>
/// The keymap. Ordered: the first row that matches the key and applies to the context wins, so
/// specific rows precede general ones. This is the only place a key's meaning is written down;
/// the router, the guide's table and the in-app help all read it.
/// </summary>
internal static class KeyBindingCatalog
{
    public static IReadOnlyList<KeyBinding> All { get; } =
    [
        // A newline, in every state, before Enter's arms can match a modified Enter.
        new(
            new KeyPattern(KeyKind.Character, Ctrl: true, Alt: false, Text: "j"),
            RoutedKey.Edit,
            KeyContext.None,
            "Insert a newline"
        ),
        new(
            new KeyPattern(KeyKind.Enter, Ctrl: false, Alt: false, Shift: false),
            RoutedKey.FollowUp,
            KeyContext.Running,
            "Queue this message as a follow-up"
        ),
        new(
            new KeyPattern(KeyKind.Enter, Ctrl: false, Alt: false, Shift: false),
            RoutedKey.Submit,
            KeyContext.Idle,
            "Send the message"
        ),
        new(
            new KeyPattern(KeyKind.Enter, Ctrl: false, Alt: false, Shift: false),
            RoutedKey.Submit,
            KeyContext.Bash,
            "Run the bash line"
        ),
        new(
            new KeyPattern(KeyKind.Enter, Alt: true),
            RoutedKey.Edit,
            KeyContext.None,
            "Insert a newline"
        ),
        new(
            new KeyPattern(KeyKind.Enter, Shift: true),
            RoutedKey.Edit,
            KeyContext.None,
            "Insert a newline"
        ),
        new(
            new KeyPattern(KeyKind.Enter, Ctrl: true),
            RoutedKey.Edit,
            KeyContext.None,
            "Insert a newline"
        ),
        new(
            new KeyPattern(KeyKind.Escape),
            RoutedKey.Cancel,
            KeyContext.None,
            "Interrupt the turn"
        ),
        new(
            new KeyPattern(KeyKind.Character, Ctrl: true, Alt: false, Text: "c"),
            RoutedKey.Interrupt,
            KeyContext.Running,
            "Interrupt the turn"
        ),
        new(
            new KeyPattern(KeyKind.Character, Ctrl: true, Alt: false, Text: "c"),
            RoutedKey.Interrupt,
            KeyContext.Bash,
            "Interrupt the command"
        ),
        new(
            new KeyPattern(KeyKind.Character, Ctrl: true, Alt: false, Text: "c"),
            RoutedKey.ClearOrQuit,
            KeyContext.None,
            "Clear the input, or quit on a second press"
        ),
        new(
            new KeyPattern(KeyKind.Character, Ctrl: true, Alt: false, Text: "d"),
            RoutedKey.Exit,
            KeyContext.None,
            "Quit on a second press within 800 ms"
        ),
        new(
            new KeyPattern(KeyKind.Character, Ctrl: true, Alt: false, Text: "l"),
            RoutedKey.Redraw,
            KeyContext.None,
            "Redraw the prompt"
        ),
        new(
            new KeyPattern(KeyKind.Character, Alt: true, Ctrl: false, Text: "p"),
            RoutedKey.ModelPicker,
            KeyContext.None,
            "Choose a model"
        ),
        new(
            new KeyPattern(KeyKind.Character, Ctrl: true, Alt: false, Text: "o"),
            RoutedKey.TranscriptViewer,
            KeyContext.None,
            "Open the transcript viewer"
        ),
        new(
            new KeyPattern(KeyKind.Tab, Ctrl: false, Alt: false, Shift: false),
            RoutedKey.Complete,
            KeyContext.None,
            "Complete a command or path"
        ),
        new(
            new KeyPattern(KeyKind.Up, Alt: false, Ctrl: false),
            RoutedKey.FollowUpTakeBack,
            KeyContext.HasQueue,
            "Take the queued messages back"
        ),
        new(
            new KeyPattern(KeyKind.Up),
            RoutedKey.HistoryPrevious,
            KeyContext.None,
            "Previous entry"
        ),
        new(new KeyPattern(KeyKind.Down), RoutedKey.HistoryNext, KeyContext.None, "Next entry"),
    ];
}

namespace Lunate.Tui;

/// <summary>What a key means to the prompt: exactly one intent per key, per the guide's table.</summary>
public enum RoutedKey
{
    Edit,
    Submit,
    Cancel,
    ClearOrQuit,
    ModelPicker,
    HistoryPrevious,
    HistoryNext,
}

/// <summary>
/// Pure classification of decoded <see cref="KeyEvent"/>s into prompt intents. Newlines
/// (Alt+Enter, Ctrl+J, and best-effort Shift+Enter) stay <see cref="RoutedKey.Edit"/> so
/// <see cref="InputLine"/> handles them; Ctrl+C routes to <see cref="RoutedKey.ClearOrQuit"/>
/// and never to cancel.
/// </summary>
public static class KeyRouter
{
    public static RoutedKey Route(KeyEvent key) =>
        key switch
        {
            { Kind: KeyKind.Character, Ctrl: true, Alt: false, Text: "j" } => RoutedKey.Edit,
            { Kind: KeyKind.Enter, Ctrl: false, Alt: false, Shift: false } => RoutedKey.Submit,
            { Kind: KeyKind.Enter } => RoutedKey.Edit,
            { Kind: KeyKind.Escape } => RoutedKey.Cancel,
            { Kind: KeyKind.Character, Ctrl: true, Alt: false, Text: "c" } => RoutedKey.ClearOrQuit,
            { Kind: KeyKind.Character, Ctrl: true, Alt: false, Text: "l" } => RoutedKey.ModelPicker,
            { Kind: KeyKind.Up } => RoutedKey.HistoryPrevious,
            { Kind: KeyKind.Down } => RoutedKey.HistoryNext,
            _ => RoutedKey.Edit,
        };
}

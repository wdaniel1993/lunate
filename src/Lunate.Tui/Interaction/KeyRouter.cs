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
    Complete,
    TranscriptViewer,
    FavoritesCycle,
    FollowUp,
    FollowUpTakeBack,
}

/// <summary>
/// Pure classification of decoded <see cref="KeyEvent"/>s into prompt intents. Enter is the send
/// key in both states: it submits an idle prompt and, while a turn runs, queues a follow-up.
/// Alt+Enter, Shift+Enter and Ctrl+J insert a newline in every state. Ctrl+C routes to
/// <see cref="RoutedKey.ClearOrQuit"/> and never to cancel.
/// </summary>
public static class KeyRouter
{
    public static RoutedKey Route(
        KeyEvent key,
        bool turnRunning = false,
        bool hasQueuedFollowUps = false
    ) =>
        key switch
        {
            { Kind: KeyKind.Character, Ctrl: true, Alt: false, Text: "j" } => RoutedKey.Edit,
            { Kind: KeyKind.Enter, Ctrl: false, Alt: false, Shift: false } when turnRunning =>
                RoutedKey.FollowUp,
            { Kind: KeyKind.Enter, Ctrl: false, Alt: false, Shift: false } => RoutedKey.Submit,
            { Kind: KeyKind.Enter } => RoutedKey.Edit,
            { Kind: KeyKind.Escape } => RoutedKey.Cancel,
            { Kind: KeyKind.Character, Ctrl: true, Alt: false, Text: "c" } => RoutedKey.ClearOrQuit,
            { Kind: KeyKind.Character, Ctrl: true, Alt: false, Text: "l" } => RoutedKey.ModelPicker,
            { Kind: KeyKind.Character, Ctrl: true, Alt: false, Shift: false, Text: "p" } =>
                RoutedKey.FavoritesCycle,
            { Kind: KeyKind.Character, Ctrl: true, Alt: false, Text: "o" } =>
                RoutedKey.TranscriptViewer,
            { Kind: KeyKind.Tab, Ctrl: false, Alt: false, Shift: false } => RoutedKey.Complete,
            { Kind: KeyKind.Up, Alt: false, Ctrl: false } when hasQueuedFollowUps =>
                RoutedKey.FollowUpTakeBack,
            { Kind: KeyKind.Up } => RoutedKey.HistoryPrevious,
            { Kind: KeyKind.Down } => RoutedKey.HistoryNext,
            _ => RoutedKey.Edit,
        };
}

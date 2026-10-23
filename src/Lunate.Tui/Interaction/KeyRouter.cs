namespace Lunate.Tui;

/// <summary>What a key means to the prompt: exactly one intent per key, per the guide's table.</summary>
public enum RoutedKey
{
    Edit,
    Submit,
    Cancel,
    ClearOrQuit,
    Interrupt,
    Exit,
    Redraw,
    ModelPicker,
    HistoryPrevious,
    HistoryNext,
    Complete,
    TranscriptViewer,
    FollowUp,
    FollowUpTakeBack,
}

/// <summary>
/// Pure classification of decoded <see cref="KeyEvent"/>s into prompt intents. The meaning of every
/// key lives in <see cref="KeyBindingCatalog"/>; this type only maps the session's state onto the
/// catalog's contexts and returns the first applicable row. Enter is the send key in both states:
/// it submits an idle prompt and, while a turn runs, queues a follow-up. Alt+Enter, Shift+Enter and
/// Ctrl+J insert a newline in every state.
/// </summary>
public static class KeyRouter
{
    public static RoutedKey Route(
        KeyEvent key,
        bool turnRunning = false,
        bool hasQueuedFollowUps = false,
        bool bashActive = false
    ) =>
        Route(
            key,
            (
                turnRunning ? KeyContext.Running
                : bashActive ? KeyContext.None
                : KeyContext.Idle
            )
                | (bashActive ? KeyContext.Bash : KeyContext.None)
                | (hasQueuedFollowUps ? KeyContext.HasQueue : KeyContext.None)
        );

    internal static RoutedKey Route(KeyEvent key, KeyContext context)
    {
        foreach (KeyBinding binding in KeyBindingCatalog.All)
        {
            if (binding.Key.Matches(key) && binding.AppliesTo(context))
            {
                return binding.Intent;
            }
        }

        return RoutedKey.Edit;
    }
}

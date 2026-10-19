namespace Lunate.Tui;

/// <summary>Where an autocomplete row comes from; selects the row treatment (design D2).</summary>
internal enum CompletionKind
{
    Command,
    Skill,
    Extension,
    File,
}

/// <summary>
/// One autocomplete row: the text an acceptance inserts, its short description (null when the
/// source has none), the source kind and the muted directory part of file rows (null otherwise).
/// Immutable data the TUI consumes; the registry composes it from the command table, the skill
/// catalog and the extension manifests.
/// </summary>
internal sealed record CompletionItem(
    string Text,
    string? Description,
    CompletionKind Kind,
    string? Muted
)
{
    /// <summary>A file row: the path, with its directory part carried as the muted segment.</summary>
    internal static CompletionItem File(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        int slash = path.LastIndexOf('/');
        return new CompletionItem(
            path,
            null,
            CompletionKind.File,
            slash < 0 ? null : path[..(slash + 1)]
        );
    }
}

/// <summary>Which trigger produced the open list (design D6).</summary>
internal enum CompletionMode
{
    Command,
    File,
}

/// <summary>
/// The open autocomplete list as the live-area state carries it: the already-filtered items (at
/// most <see cref="MaxRows"/>), the selected index, the mode and where the accepted text replaces
/// the input (design D4). The model is data; the session owns opening, navigation and acceptance.
/// </summary>
internal sealed record AutocompleteState(
    IReadOnlyList<CompletionItem> Items,
    int SelectedIndex,
    CompletionMode Mode,
    int TokenStart
)
{
    /// <summary>The most rows the list ever holds (design D4).</summary>
    internal const int MaxRows = 8;

    /// <summary>The selected item, or null when the list holds none.</summary>
    internal CompletionItem? Selected =>
        Items.Count == 0 ? null : Items[Math.Clamp(SelectedIndex, 0, Items.Count - 1)];

    /// <summary>Moves the selection by <paramref name="delta"/>; clamped at both ends, no wrap.</summary>
    internal AutocompleteState Move(int delta)
    {
        if (Items.Count == 0)
        {
            return this with { SelectedIndex = 0 };
        }

        int next = Math.Clamp(SelectedIndex + delta, 0, Items.Count - 1);
        return next == SelectedIndex ? this : this with { SelectedIndex = next };
    }
}

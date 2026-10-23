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
        string trimmed = path.Length > 1 && path.EndsWith('/') ? path[..^1] : path;
        int slash = trimmed.LastIndexOf('/');
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
    int TokenStart,
    int HiddenCount = 0
)
{
    /// <summary>The most rows the list ever holds, indicator included (design D4).</summary>
    internal const int MaxRows = 8;

    /// <summary>
    /// Caps a filtered list to the live-area budget. When entries do not fit, the last row is the
    /// "more" indicator, so one fewer item is shown — never eight items and a silently hidden tail.
    /// </summary>
    internal static (IReadOnlyList<CompletionItem> Rows, int Hidden) Cap(
        IReadOnlyList<CompletionItem> items
    )
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count <= MaxRows)
        {
            return (items, 0);
        }

        int shown = MaxRows - 1;
        return ([.. items.Take(shown)], items.Count - shown);
    }

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

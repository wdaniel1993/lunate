namespace Lunate.Tui;

/// <summary>What a pick-mode key did: nothing, closed without a pick, or picked an entry.</summary>
internal enum PickOutcome
{
    None,
    Closed,
    Picked,
}

/// <summary>
/// The transcript viewer's pick mode — what <c>/tree</c> opens. Same alternate screen, same
/// rendering (the renderer paints the document it always paints, with the selected entry marked):
/// <c>j</c>/<c>k</c> move between pickable entries, <c>/</c> filters by text, <c>Enter</c> picks one
/// and closes, and <c>q</c>/<c>Esc</c>/<c>Ctrl+O</c> close without picking. Picking never sends
/// anything: the caller decides what the picked entry means.
/// </summary>
internal sealed partial class TranscriptViewer
{
    private const string PickHintUnicode =
        "/ filter \u00b7 j/k move \u00b7 enter pick \u00b7 q close";
    private const string PickHintAscii = "/ filter - j/k move - enter pick - q close";
    private const string FilterHintUnicode = "type to filter \u00b7 enter confirm \u00b7 esc clear";
    private const string FilterHintAscii = "type to filter - enter confirm - esc clear";

    private PickState? _pick;
    private int? _pickedEntryIndex;

    /// <summary>The entry index the caller's pickable list resolved to; null until a pick.</summary>
    internal int? PickedEntryIndex => _pickedEntryIndex;

    /// <summary>Whether the viewer is in pick mode rather than plain browsing.</summary>
    internal bool IsPicking => _pick is not null;

    /// <summary>
    /// Opens the viewer in pick mode over <paramref name="transcript"/>, whose pickable entries are
    /// the indices in <paramref name="pickableEntryIndices"/>. The first pickable entry starts
    /// selected; with none, the frame still opens and Enter does nothing.
    /// </summary>
    internal void Pick(TranscriptModel transcript, IReadOnlyList<int> pickableEntryIndices)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(pickableEntryIndices);
        if (IsOpen)
        {
            return;
        }

        _pickedEntryIndex = null;
        _pick = new PickState(transcript, pickableEntryIndices);
        RebuildView();
        _scrolledLines = SelectionScrollLines(_transcript!);
        Open(_transcript!);
    }

    /// <summary>
    /// One key in pick mode. Returns <see cref="PickOutcome.Picked"/> with
    /// <see cref="PickedEntryIndex"/> set when an entry was chosen, <see cref="PickOutcome.Closed"/>
    /// when the viewer was dismissed, and <see cref="PickOutcome.None"/> while it stays open.
    /// </summary>
    internal PickOutcome HandlePickKey(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_pick is not { } pick || !IsOpen)
        {
            return PickOutcome.Closed;
        }

        if (pick.Filtering)
        {
            return HandleFilterKey(key, pick);
        }

        switch (key)
        {
            case { Kind: KeyKind.Escape }:
            case { Kind: KeyKind.Character, Ctrl: true, Alt: false, Text: "o" }:
            case { Kind: KeyKind.Character, Ctrl: false, Alt: false, Text: "q" }:
                Close();
                return PickOutcome.Closed;
            case { Kind: KeyKind.Down }:
            case { Kind: KeyKind.Character, Ctrl: false, Alt: false, Text: "j" }:
                Move(pick, 1);
                return PickOutcome.None;
            case { Kind: KeyKind.Up }:
            case { Kind: KeyKind.Character, Ctrl: false, Alt: false, Text: "k" }:
                Move(pick, -1);
                return PickOutcome.None;
            case { Kind: KeyKind.Character, Ctrl: false, Alt: false, Text: "/" }:
                pick.Filtering = true;
                pick.Filter = string.Empty;
                RebuildView();
                return PickOutcome.None;
            case { Kind: KeyKind.Enter }:
                return Confirm(pick);
            default:
                return PickOutcome.None;
        }
    }

    /// <summary>The pick-mode view options; null while the viewer browses.</summary>
    private TranscriptViewOptions? ViewOptions() =>
        _pick is not { } pick
            ? null
            : new TranscriptViewOptions(
                SelectedEntry: pick.SelectedEntryIndex,
                Position: pick.Pickable.Count == 0 ? 0 : pick.Selected + 1,
                Count: pick.Pickable.Count,
                Hint: pick.Filtering
                    ? (_capabilities.Unicode ? FilterHintUnicode : FilterHintAscii)
                    : (_capabilities.Unicode ? PickHintUnicode : PickHintAscii)
            );

    private PickOutcome HandleFilterKey(KeyEvent key, PickState pick)
    {
        switch (key)
        {
            case { Kind: KeyKind.Escape }:
                pick.Filtering = false;
                pick.Filter = string.Empty;
                pick.Selected = 0;
                RebuildView();
                return PickOutcome.None;
            case { Kind: KeyKind.Backspace }:
                if (pick.Filter.Length > 0)
                {
                    pick.Filter = pick.Filter[..^1];
                    pick.Selected = 0;
                    RebuildView();
                }

                return PickOutcome.None;
            case { Kind: KeyKind.Enter }:
                return Confirm(pick);
            case { Kind: KeyKind.Character, Ctrl: false, Alt: false, Text: { Length: 1 } text }:
                pick.Filter += text;
                pick.Selected = 0;
                RebuildView();
                return PickOutcome.None;
            default:
                return PickOutcome.None;
        }
    }

    /// <summary>
    /// Rebuilds the painted model for the current filter: every entry while the filter is empty, the
    /// text matches otherwise. The selection clamps into the visible pickable rows.
    /// </summary>
    private void RebuildView()
    {
        if (_pick is not { } pick)
        {
            return;
        }

        HashSet<int> pickable = [.. pick.PickableIndices];
        List<TranscriptEntry> visible = [];
        List<int> visiblePickable = [];
        List<int> originalPickable = [];
        for (var index = 0; index < pick.Full.Entries.Count; index++)
        {
            TranscriptEntry entry = pick.Full.Entries[index];
            if (pick.Filter.Length > 0 && !Matches(entry, pick.Filter))
            {
                continue;
            }

            if (pickable.Contains(index))
            {
                visiblePickable.Add(visible.Count);
                originalPickable.Add(index);
            }

            visible.Add(entry);
        }

        pick.Pickable = visiblePickable;
        pick.PickableOriginal = originalPickable;
        pick.Selected = Math.Clamp(pick.Selected, 0, Math.Max(0, visiblePickable.Count - 1));
        _transcript = new TranscriptModel(visible);
        ScrollToSelection();
    }

    private void Move(PickState pick, int delta)
    {
        if (pick.Pickable.Count == 0)
        {
            return;
        }

        pick.Selected = Math.Clamp(pick.Selected + delta, 0, pick.Pickable.Count - 1);
        ScrollToSelection();
    }

    private PickOutcome Confirm(PickState pick)
    {
        if (pick.Pickable.Count == 0)
        {
            return PickOutcome.None;
        }

        _pickedEntryIndex = pick.PickableOriginal[pick.Selected];
        Close();
        return PickOutcome.Picked;
    }

    /// <summary>
    /// Puts the selected entry's first line at the top of the window when the document is tall
    /// enough, so <c>j</c>/<c>k</c> always move the frame and the marked row stays visible. Never
    /// paints: used before the alternate screen exists.
    /// </summary>
    private int SelectionScrollLines(TranscriptModel transcript)
    {
        if (_pick is not { SelectedEntryIndex: { } entry } pick)
        {
            return 0;
        }

        ConsoleSize size = _console.Size;
        IReadOnlyList<int> tops = _renderer.EntryTops(transcript, size);
        if (entry >= tops.Count)
        {
            return _scrolledLines;
        }

        int maxTop = _renderer.MaxScrolledLines(transcript, size);
        return Math.Clamp(maxTop - tops[entry], 0, maxTop);
    }

    /// <summary>Moves the window to the selection and repaints, while the viewer owns the screen.</summary>
    private void ScrollToSelection()
    {
        if (_pick is null || _transcript is not { } transcript)
        {
            return;
        }

        _scrolledLines = SelectionScrollLines(transcript);
        if (IsOpen)
        {
            Paint(transcript);
        }
    }

    /// <summary>A filter matches an entry's own text: a user message's text or an assistant paragraph.</summary>
    private static bool Matches(TranscriptEntry entry, string filter) =>
        entry switch
        {
            TranscriptUserEntry user => user.Text.Contains(
                filter,
                StringComparison.OrdinalIgnoreCase
            ),
            TranscriptAssistantEntry assistant => assistant.Markdown.Contains(
                filter,
                StringComparison.OrdinalIgnoreCase
            ),
            TranscriptTreeRowEntry row => row.Text.Contains(
                filter,
                StringComparison.OrdinalIgnoreCase
            ) || row.Id.Contains(filter, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    /// <summary>The pick mode's state: the full model, the pickable rows, the selection and filter.</summary>
    private sealed class PickState(TranscriptModel full, IReadOnlyList<int> pickableIndices)
    {
        public TranscriptModel Full { get; } = full;

        public IReadOnlyList<int> PickableIndices { get; } = pickableIndices;

        public List<int> Pickable { get; set; } = [];

        /// <summary>The caller's indices for the pickable rows, parallel to <see cref="Pickable"/>.</summary>
        public List<int> PickableOriginal { get; set; } = [];

        public int Selected { get; set; }

        public string Filter { get; set; } = string.Empty;

        public bool Filtering { get; set; }

        /// <summary>The marked entry index in the painted model, or null when there is none.</summary>
        public int? SelectedEntryIndex =>
            Pickable.Count == 0 ? null : Pickable[Math.Clamp(Selected, 0, Pickable.Count - 1)];
    }
}

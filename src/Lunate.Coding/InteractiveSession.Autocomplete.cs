using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    private AutocompleteState? _autocomplete;
    private bool _autocompleteDismissed;
    private bool _indexingNoticeShown;

    /// <summary>The open autocomplete list; null while closed (the tests' view of the live state).</summary>
    internal AutocompleteState? Autocomplete => _autocomplete;

    /// <summary>The input line as typed so far (the tests' view).</summary>
    internal InputLineState Input => _input;

    /// <summary>
    /// Recomputes the open list after an edit or cursor move (design D6/D8): command mode first,
    /// else file mode, else closed. The query drives the fuzzy filter and the row cap.
    /// </summary>
    /// <summary>
    /// Recomputes the open list after an edit or cursor move (design D6/D8): command mode first,
    /// else file mode, else closed. The query drives the fuzzy filter and the row cap. A text edit
    /// re-arms a list dismissed with Esc; a bare cursor move does not.
    /// </summary>
    private void RefreshAutocomplete(bool edited)
    {
        if (edited)
        {
            _autocompleteDismissed = false;
        }

        if (!_composed || _turnActive)
        {
            CloseAutocomplete();
            return;
        }

        if (TryCommandQuery(_input, out string query, out int tokenStart))
        {
            if (_autocompleteDismissed)
            {
                CloseAutocomplete();
                return;
            }

            SetAutocomplete(
                CompletionMode.Command,
                tokenStart,
                FuzzyMatch.Filter(_commands.CompletionItems, query)
            );
            return;
        }

        if (TryPathPrefix(_input.Text, _input.CursorPosition, out string prefix, out tokenStart))
        {
            if (_autocompleteDismissed)
            {
                CloseAutocomplete();
                return;
            }

            RefreshFileAutocomplete(prefix, tokenStart);
            return;
        }

        _autocompleteDismissed = false;
        CloseAutocomplete();
    }

    /// <summary>
    /// The file trigger (design D7): the lazy index's case-insensitive completion query, capped
    /// at eight rows; while the index builds the session shows the indexing notice and starts
    /// the build.
    /// </summary>
    private void RefreshFileAutocomplete(string prefix, int tokenStart)
    {
        FileIndex index = FileIndex();
        if (!index.IsReady)
        {
            index.EnsureStarted();
            if (!_indexingNoticeShown)
            {
                _indexingNoticeShown = true;
                Notify(_capabilities.Unicode ? "file index: indexing…" : "file index: indexing...");
            }

            CloseAutocomplete();
            return;
        }

        if (_indexingNoticeShown)
        {
            _indexingNoticeShown = false;
            _live.SetNotice(null);
        }

        IReadOnlyList<string> matches = index.Complete(prefix, AutocompleteState.MaxRows);
        SetAutocomplete(CompletionMode.File, tokenStart, [.. matches.Select(CompletionItem.File)]);
    }

    /// <summary>The command trigger: a line starting with `/`, cursor inside the first word.</summary>
    private static bool TryCommandQuery(InputLineState input, out string query, out int tokenStart)
    {
        query = string.Empty;
        tokenStart = 0;
        string text = input.Text;
        int cursor = Math.Clamp(input.CursorPosition, 0, text.Length);
        if (cursor == 0 || text.Length == 0 || text[0] != '/')
        {
            return false;
        }

        for (var index = 0; index < cursor; index++)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                return false;
            }
        }

        query = text[..cursor];
        return true;
    }

    /// <summary>Publishes the filtered list, capped at eight rows; an empty result closes it.</summary>
    private void SetAutocomplete(
        CompletionMode mode,
        int tokenStart,
        IReadOnlyList<CompletionItem> items
    )
    {
        if (items.Count == 0)
        {
            CloseAutocomplete();
            return;
        }

        IReadOnlyList<CompletionItem> rows =
            items.Count <= AutocompleteState.MaxRows
                ? items
                : [.. items.Take(AutocompleteState.MaxRows)];
        int selected = 0;
        if (_autocomplete?.Selected is { } previous)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                if (rows[index].Text == previous.Text && rows[index].Kind == previous.Kind)
                {
                    selected = index;
                    break;
                }
            }
        }

        var state = new AutocompleteState(rows, selected, mode, tokenStart);
        _autocomplete = state;
        _live.SetAutocomplete(state);
    }

    /// <summary>Closes the list and clears the live-area state.</summary>
    private void CloseAutocomplete()
    {
        if (_autocomplete is null)
        {
            return;
        }

        _autocomplete = null;
        _live.SetAutocomplete(null);
    }

    /// <summary>Esc: closes the list and keeps it closed until the next text edit.</summary>
    private void DismissAutocomplete()
    {
        _autocompleteDismissed = true;
        CloseAutocomplete();
    }

    /// <summary>Moves the open list's selection; clamped, no wrap (design D5).</summary>
    private void MoveAutocomplete(int delta)
    {
        if (_autocomplete is not { } state)
        {
            return;
        }

        AutocompleteState moved = state.Move(delta);
        if (moved == state)
        {
            return;
        }

        _autocomplete = moved;
        _live.SetAutocomplete(moved);
    }

    /// <summary>
    /// Tab / Enter acceptance (design D5): replaces <see cref="AutocompleteState.TokenStart"/> to
    /// the end of the cursor's word with the item text — files keep the <c>@</c>, commands get a
    /// trailing space — and closes the list. Accepting mid-word replaces the whole word, so no
    /// tail is stranded. An exact match is left alone; Enter then submits as usual.
    /// </summary>
    private void AcceptAutocomplete()
    {
        if (_autocomplete is not { } state || state.Selected is not { } item)
        {
            CloseAutocomplete();
            return;
        }

        string text = _input.Text;
        int cursor = Math.Clamp(_input.CursorPosition, 0, text.Length);
        int start = Math.Clamp(state.TokenStart, 0, cursor);
        int end = cursor;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        string replacement = state.Mode is CompletionMode.File ? "@" + item.Text : item.Text + " ";
        string updated = text[..start] + replacement + text[end..];

        CloseAutocomplete();
        _autocompleteDismissed = false;
        _input = new InputLineState(updated, start + replacement.Length);
        _historyDraft = null;
        _live.SetInput(_input);
    }

    /// <summary>
    /// Whether Enter would only re-insert what is already typed: the selected row equals the
    /// token. Enter then submits instead of accepting a no-op completion. Tab still accepts.
    /// </summary>
    private bool IsExactAutocompleteMatch()
    {
        if (_autocomplete is not { } state || state.Selected is not { } item)
        {
            return false;
        }

        int cursor = Math.Clamp(_input.CursorPosition, 0, _input.Text.Length);
        int start = Math.Clamp(state.TokenStart, 0, cursor);
        string token = _input.Text[start..cursor];
        string accepted = state.Mode is CompletionMode.File ? "@" + item.Text : item.Text;
        return string.Equals(token, accepted, StringComparison.Ordinal);
    }

    /// <summary>Whether a key changes the text (and so re-arms a dismissed list).</summary>
    private static bool IsTextEdit(KeyEvent key) =>
        key.Kind
            is KeyKind.Character
                or KeyKind.Space
                or KeyKind.Paste
                or KeyKind.Backspace
                or KeyKind.Delete
                or KeyKind.Enter;

    /// <summary>The lazily created index; the seam falls back to the real workspace walk.</summary>
    private FileIndex FileIndex() =>
        _fileIndex ??= new FileIndex(
            _options.WorkspaceFiles ?? new SystemWorkspaceFiles(_workspace.WorktreeRoot)
        );

    /// <summary>The token ending at <paramref name="cursor"/> when it starts with `@`.</summary>
    private static bool TryPathPrefix(
        string text,
        int cursor,
        out string prefix,
        out int tokenStart
    )
    {
        prefix = string.Empty;
        tokenStart = 0;
        if (cursor < 0 || cursor > text.Length)
        {
            return false;
        }

        if (cursor < text.Length && !char.IsWhiteSpace(text[cursor]))
        {
            return false;
        }

        int start = cursor;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        if (start >= cursor || text[start] != '@')
        {
            return false;
        }

        tokenStart = start;
        prefix = text[(start + 1)..cursor];
        return true;
    }
}

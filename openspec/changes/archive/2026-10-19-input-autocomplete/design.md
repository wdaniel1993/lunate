# Design — input box and autocomplete

## D1. The input box chrome

The live area's frame order becomes: transient state (thinking, tools, notices, prompts) → **rule** (≥80 columns) → input lines → **rule** (≥80) → status footer. The rules are `─` runs spanning the width (ASCII `-` without Unicode; none below 80 columns). The input's first line keeps the `> ` prompt prefix; continuation lines keep the two-space indent (existing `InputLine.Layout`). When the input is empty, the first line renders the placeholder `Ask anything, / for commands` in the muted style instead of the cursor-only line. The box grows with the input up to 8 lines total and then scrolls (the existing `maxLines` layout behaviour); the footer always stays visible below.

## D2. The registry — one source, composed once per session

A `CompletionRegistry` (Coding side) composed at session start from exactly three sources plus the file index:

- **Built-in commands**: `Commands.Descriptors` — name + description + group (the table the `/help` view already renders; no second list).
- **Skills**: the session's discovered skills — `/skill:<name>` + the skill's description when it has one.
- **Extension commands**: every loaded extension's `Manifest.Commands` names, described by the extension's id.
- **Files**: the existing lazy `FileIndex` (its ordinal `Match` query), queried per `@` token — never materialised into the registry.

Items are immutable records (`CompletionItem(Text, Description, Kind, Muted)`) where `Kind` selects the row treatment (command / skill / extension / file) and `Muted` marks the directory part of file rows. The TUI consumes items, never the sources.

## D3. The fuzzy matcher (pure, TUI side)

A `FuzzyMatch.Score(query, candidate)` subsequence scorer: every query character must appear in order (case-insensitive); scoring prefers prefix > word-start > later positions, ties broken by shorter candidate then ordinal. `Filter(items, query)` returns the ranked subset. The existing `Completion` helper (LCP, Tab-only) is deleted; its role is absorbed here. Empty query returns the registry in its natural order (commands, skills, extensions; files in index order).

## D4. The list state and rendering

`AutocompleteState` (in the live-area state): `Items` (already filtered), `SelectedIndex`, `Mode` (Command / File), `TokenStart` (where the accepted text replaces). Rendering: at most 8 rows directly above the rule/input; row = `name` (accent when selected, plain otherwise) + two spaces + description (muted) when the width ≥ 60; file rows render the directory part muted and the basename plain. Selected row = reverse or accent-marked; the visual snapshot defines it. No matches = the list stays closed (no empty box). The list never wraps and never exceeds the width.

## D5. Key precedence while the list is open

- ↑/↓ — move the selection (clamped; wraps off). Otherwise history navigation.
- Tab / Enter — accept the selected item: replace `TokenStart..cursor` with the item text (files keep the `@`; commands get a trailing space), close the list. Otherwise Tab completes nothing and Enter submits. When the typed token already equals the selected row exactly, Enter submits (accepting would be a no-op and would cost a second Enter for every fully typed command); Tab still accepts. Newline keys insert and then re-run the trigger rules, so the list normally closes after a newline.
- Esc — close the list. Otherwise cancel the turn / clear the input as today.
- Shift+Enter / Alt+Enter / Ctrl+J — insert a newline, then the trigger rules re-run (a continuation line leaves command mode, so the list normally closes; file mode can stay open mid-line).
- Typing/backspace — re-filter; the trigger rules (D6) decide open/close.
- Ctrl+C, Ctrl+L, Ctrl+O — unchanged (they win over the list).

## D6. The trigger rules

- **Command mode**: the input's first character is `/` and the cursor sits within the first word (no whitespace between the `/` and the cursor); the query = that word. Multi-line inputs never open command mode from a continuation line.
- **File mode**: the cursor sits at the end of a non-whitespace token that starts with `@` (the existing path-token rule); the query = the text after `@`. Precedence: command mode first, else file mode (matching today's Tab order).
- Closing: mode conditions no longer hold, the query yields no items, Esc, accept, or submit.

## D7. File rows and the index

File mode queries `FileIndex.Match(query, 0)` (ordinal prefix, bounded, honouring the index contract — no client-side fuzz over the index). Not ready → the existing indexing notice + `EnsureStarted`. Rows: the matched paths, directory part muted, capped at 8 with no ellipsis row (the list is a viewport; typing narrows). The index's laziness, ignore rules, bounds and the gate budget are untouched.

## D8. Session wiring

The session keeps the registry + the current mode/query; on every input edit (and cursor move) it recomputes the filtered items and updates the live state; the acceptance path replaces the token via `TokenStart` and re-renders. The old `ApplyCompletion`/`TryCompleteSlashWord`/`CompletePathToken` paths are replaced by this state machine. The composition test drives the real host: type `/`, see the list, arrow, Tab, submit.

## D9. Breakpoints and colour

≥80: rules on, descriptions on (≥60), full footer below. <80: no rules. <60: descriptions dropped; file rows show the basename only when the directory part does not fit. Colour: selection = accent (`#5fd7d7`/16-colour cyan), descriptions muted, file directories muted; NO_COLOR = plain text with the selection marked by the `> ` pointer prefix in the list gutter (ASCII `>`). The existing capability plumbing (`ColorSystemSupport`, `CellText`) is reused; goldens cover 120/80/50 × truecolor/256/16/none.

## D10. Divergences and non-goals

- Extension-command **invocation** stays out of scope (today: an unknown slash word is a notice); the registry lists them for discoverability, which the card asks for.
- File mode uses the index's ordinal query, not fuzzy subsequence — the index contract is prefix-ordinal and bounded; fuzzing 200k entries client-side would break its budget. The registry (fixed-size) is fuzzy-filtered.
- Input history (↑/↓ when closed) is unchanged.
- The startup budget is unaffected: the registry composes from in-memory data; the file index remains lazy.

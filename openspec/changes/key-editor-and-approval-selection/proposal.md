# Key editor and approval selection

## Why

PR #124 made `KeyBindingCatalog` the single place a key's meaning is written down, and three of its promises are still prose. `Ctrl+G` is named in the spec (and in `key-parity`'s design) but no catalog row exists: pressing it does nothing. The approval panel's requirement says Up and Down move a selection and Space activates it; the shipped panel has neither a selection to see nor a key to move it, so `Up`/`Down`/`Space` are dead in the one place the user is waiting on a decision. And the guide's key table is still a hand-kept second copy of the catalog — the exact drift the catalog was introduced to remove — with six catalog keys undocumented.

Each gap is small, but each one is a place where the spec and the product disagree while both look complete, which is worse than an obviously missing feature.

## What Changes

- **`Ctrl+G` opens the draft in an external editor.** `$VISUAL`, else `$EDITOR`, else `vi` on POSIX and `notepad` on Windows; the draft goes to a temporary file, the live area is suspended and the terminal handed over in cooked mode, and the file is read back on exit. A zero exit replaces the draft with the file's text and puts the cursor at its end; a non-zero exit, a launch failure or a read-back that fails leaves the draft, the cursor and the stash untouched and posts one notice naming what failed. An empty draft opens an empty buffer. A draft that changed while the editor was open keeps the prompt, and the editor's text goes to the stash so neither is lost. The row lives in `KeyBindingCatalog`; the catalog — not the terminal, not an extension — decides what `Ctrl+G` means.
- **Approvals get a visible, movable selection.** Up and Down move a `> `-marked selection over `[y]es`, `[n]o`, `[a]lways this session`, wrapping at both ends; Space activates the selection; `y`, `n` and `a` keep deciding directly whatever the selection shows; Esc and Enter keep denying. The selection is prompt state held by the prompt model, so a resize or a repaint keeps it, and each new prompt starts on `[y]es`. At 40 columns all three choices plus the marker fit one 36-cell row; below that they stack one per row, and the detail leaves before a choice ever does.
- **A test pins the guide's table to the catalog.** `docs/guide.md`'s key table and `KeyBindingCatalog` are compared in both directions: every catalog key must appear in the table, every table key must have a catalog row. Merged cells (`Ctrl+A` / `Ctrl+E`) are read key by key; only the Key column counts, so a key named in prose is not a documented binding. Exempt, each with a stated reason: the panel-owned approval letters `y`, `n`, `a`, the prompt-gated `?`, and the deliberately unbound `Ctrl+P`. A missing table fails instead of passing vacuously.

## Capabilities

### New Capabilities

None. All three items are prompt-surface behaviour, and the prompt surface is already `tui`.

### Modified Capabilities

- `tui` — *Approval prompt*: the selection gains its contract (wrap, marker, prompt state, letter precedence, 40-column and height behaviour). Two requirements are added: *External editor for the draft* (the `Ctrl+G` flow end to end) and *The guide's key table matches the catalog* (the drift check, its exemptions and its failure text). Nothing about routing, contexts or the catalog's ordering rule changes; the new `Ctrl+G` row is the first row the catalog has gained since #124.

## Impact

- `src/Lunate.Tui/Interaction`: the `Ctrl+G` row and a `RoutedKey.Editor` member; `ApprovalPromptModel` gains the selection (index, move, activation) and `ApprovalPanelLayout` renders the marker, the stacked form and the clip rule.
- `src/Lunate.Tui`: `IConsoleIO` gains the terminal-suspend scope, `SystemConsoleIO` implements it over the raw-mode and key-pump lifetime, and `FakeConsoleIO` scripts it. Both public types need `PublicAPI.Unshipped.txt` entries.
- `src/Lunate.Coding`: the session runs the child, owns the temp file, and dispatches the editor intent; the approval branch re-posts the prompt when the selection moves.
- `docs/guide.md`: the `Ctrl+G` row, the `Ctrl+P` row, the approval letters row, the six undocumented catalog keys folded into their cells, and the approvals paragraph gains the selection.
- `tests/Lunate.Tui.Tests`: the editor flow against a scripted child, the approval selection and its golden frames, and `GuideKeyTableTests`. The scripted child is a test executable or an inline script, never a real editor.
- No new packages, no new project references, no ADR: every decision here is local to the interaction layer, and the catalog stays the one table an ADR would otherwise have to protect.

# Tasks

## 1. The catalog

- [ ] 1.1 `KeyBinding`, `KeyContext` (flags) and `KeyBindingCatalog` in `Lunate.Tui`; ordered rows with a description each.
- [ ] 1.2 `RoutedKey` gains the editor and session intents; `KeyRouter.Route` walks the catalog (same signature, `turnRunning`/`hasQueuedFollowUps` map to contexts).
- [ ] 1.3 Matrix test: every row routes to its own intent; no two rows overlap within a context; every `RoutedKey` member is reachable; every row has a non-empty description.
- [ ] 1.4 Guide table check: parse `docs/guide.md`'s key table, assert it matches the catalog row for row.

## 2. Ctrl+C, Ctrl+D, Ctrl+L, Ctrl+P

- [ ] 2.1 `Ctrl+C`: interrupt when a turn runs, clear when idle with text, arm the quit window when empty idle; the quit window keeps its injected scheduler.
- [ ] 2.2 `Ctrl+D`: 800 ms double-press exit, armed only when idle and empty; ignored mid-turn (documented divergence).
- [ ] 2.3 `Ctrl+L`: full redraw preserving input and conversation; the model picker moves to `Alt+P`.
- [ ] 2.4 Remove favourites cycling; `Ctrl+P` unbound; the picker still lists favourites.
- [ ] 2.5 Tests: interrupt cancels a running turn and keeps the queue; clear vs arm; Ctrl+D window boundaries on virtual time; Ctrl+L keeps the buffer; Alt+P opens the picker; Ctrl+P does nothing.

## 3. Prompt capabilities

- [ ] 3.1 `Ctrl+R` history search: filter over history, next-match key, accept, cancel; reuses the autocomplete list rendering.
- [ ] 3.2 `Ctrl+G` external editor: temp file, suspend/restore, `$VISUAL`/`$EDITOR`/`vi`, read back, notice on failure.
- [ ] 3.3 `Ctrl+S` stash/restore with a single slot and a notice when replacing.
- [ ] 3.4 `Ctrl+_` undo with a bounded 64-entry stack; cleared on submit and newline.

## 4. Line editor

- [ ] 4.1 `Ctrl+A`/`Ctrl+E` line start/end; `Alt+B`/`Alt+F` word motion (cell-aware, never splitting a surrogate pair).
- [ ] 4.2 `Ctrl+K`/`Ctrl+U`/`Ctrl+W`/`Alt+D` kill operations and `Ctrl+Y` yank, with a kill ring of one entry.
- [ ] 4.3 Tests: each operation on ASCII, CJK and combining marks; kill/yank round-trip; undo restores the exact buffer and cursor.

## 5. Approvals

- [ ] 5.1 `Esc` denies; `Up`/`Down` move a selection rendered on the choices; `Space` activates the selection; `y`/`n`/`a` unchanged; Enter still denies.
- [ ] 5.2 Tests: selection movement wraps, Space activates the highlighted choice, Esc denies, Enter still denies, 40-column layout keeps the selection visible.

## 6. Docs and media

- [ ] 6.1 `docs/guide.md` key table rewritten from the catalog; new rows for the editor keys, history search, external editor, stash, undo; Ctrl+C dispatch explained.
- [ ] 6.2 A `features-keys.tape` covering the visible ones (Ctrl+L redraw, Ctrl+R search, stash/restore).
- [ ] 6.3 `openspec validate key-parity --strict`; formatter; the fast gate.

## 7. Verification

- [ ] 7.1 Full `scripts/verify.sh` green on both culture passes; CI green on four platforms.

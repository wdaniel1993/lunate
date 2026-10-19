# Tasks — input box and autocomplete

## 1. Registry and matcher

- [x] 1.1 `CompletionItem` + `CompletionRegistry` (Coding): composed from `Commands.Descriptors`, the skill catalog (`/skill:<name>` + descriptions), the extension manifests' commands (described by extension id); unit tests for composition order, dedupe and the absence of any second list.
- [ ] 1.2 `FuzzyMatch` (Tui, pure): subsequence scoring (prefix > word-start > position; shorter-then-ordinal tie-breaks), `Filter` over items; unit tests incl. empty query (natural order), no-match, case-insensitivity, unicode.
- [ ] 1.3 Delete the old `Completion` helper (LCP/Tab-only) and its call sites; update its tests to the new engine.

## 2. State and session wiring

- [ ] 2.1 `AutocompleteState` in the live-area state (items, selected index, mode, token start); the session recomputes on edit/cursor move (D6 triggers; command mode first, file mode second).
- [ ] 2.2 Key precedence (D5): ↑/↓ move vs history; Tab/Enter accept vs complete-nothing/submit; Esc closes vs cancel; newline keys keep inserting; Ctrl+C/L/O unchanged. Session tests for each precedence pair.
- [ ] 2.3 File mode through the lazy index (D7): query via `FileIndex.Match`, indexing notice while building, rows capped at 8.
- [ ] 2.4 Replace `ApplyCompletion`/`TryCompleteSlashWord`/`CompletePathToken` with the state machine; keep the routing (`RoutedKey.Complete`) intact.

## 3. Rendering

- [ ] 3.1 Box chrome (D1): rules ≥80, placeholder when empty, footer below the box, frame-order change; unit tests at 120/80/50.
- [ ] 3.2 The list rows (D4/D9): ≤8 rows above the input, selection styling, descriptions ≥60, muted file directories, ASCII fallbacks; unit tests per breakpoint × colour mode.

## 4. Tests and media

- [ ] 4.1 Snapshot goldens: box states (empty/filled/multi-line) and list states (command mode, file mode, selected rows, no-match) at 120/80/50 × truecolor/256/16/none.
- [ ] 4.2 Production composition test: the real host renders the box, types `/`, shows the filtered list, arrows, accepts with Tab, submits; and the `@` flow with the index. Deterministic (replay client + the hook gate pattern).
- [ ] 4.3 VHS tape (`features-input`) driven by the replay client: the box, `/` list, accept, `@` list; GIF + README gallery entry.
- [ ] 4.4 Startup budget unchanged (registry cheap; index lazy) — verified by the change gate.

## 5. Docs

- [ ] 5.1 `docs/guide.md`: the input box, the autocomplete (registry, keys, breakpoints) and the placeholder.
- [ ] 5.2 README: the keybindings/features mention (the gallery entry from 4.3).

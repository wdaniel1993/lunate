# Tasks: Interactive session — commands, pickers, completion (T-22, part 2)

## 1. SelectList + live-area seam (Tui)

- [ ] 1.1 `SelectListModel` + `SelectListRenderer` (title, `>` marker, current-item `*`, window of at most 8 items, deterministic golden)
- [ ] 1.2 `LiveArea.SetPicker` + `PickerInput` (the approval pattern); render test
- [ ] 1.3 `KeyRouter`: bare Tab → `RoutedKey.Complete`; Shift+Tab stays `Edit`; routing matrix updated

## 2. Completion helper (Tui)

- [ ] 2.1 `Completion.Complete(text, candidates)` pure (one match → full; several → longest common prefix; no progress → candidate list); unit tests incl. synthetic colliding prefixes

## 3. Session listing (Agent)

- [ ] 3.1 `Session.List(directory)` + `SessionSummary` (header-only read, newest modified first, id tie-break, cap 20, corrupt skip, never writes); tests; PublicAPI files updated

## 4. Commands + pickers (Coding)

- [ ] 4.1 Command parsing + dispatch (skip pipeline, history, unknown-command notice); tests
- [ ] 4.2 `/quit` (same path as the double Ctrl+C; works during a turn)
- [ ] 4.3 `/compact` (idle guard; `CompactNowAsync`; true/false notices); tests
- [ ] 4.4 Picker state machine (open/keys/confirm/dismiss; idle guard; approval exclusivity); tests
- [ ] 4.5 Model picker + `/model <id>` (catalog; `AppendModelChange`; harness rebuild; footer; next request carries the history); tests
- [ ] 4.6 `/new` (fresh session + rebuild + notice); tests
- [ ] 4.7 `/resume` (`Session.List` + `Load` + rebuild + notice); tests
- [ ] 4.8 Compose refactor (core + `BuildHarness(model, session)`) so rebuilds reuse it
- [ ] 4.9 Tab-completion wiring (idle + scope checks; notice when no progress); tests

## 5. Steering echo (Coding)

- [ ] 5.1 Pending FIFO + `HandleEvent(SteeringInjected)` → dim `» <text>` commit; tests (echoed on injection; returned leftovers never echoed)

## 6. Interactive entry (Coding)

- [ ] 6.1 `Cli.Run` bare-`lunate` path + options seam + `ConsoleSupport.Check` gate + `-p` hint + exit 2; help text updated
- [ ] 6.2 Tests (scripted console runs a session; non-terminal hint; help mentions the entry)

## 7. E2E + docs + gate

- [ ] 7.1 Extend the scripted snapshot (echo, `/mod`+Tab → `/model`, picker selection, `/new`, `/resume` with id8 normalization, `/quit`); regenerate goldens deliberately
- [ ] 7.2 `docs/guide.md`: key-binding table gains the `Tab` row
- [ ] 7.3 `bash scripts/verify.sh` green; deviations recorded in design.md

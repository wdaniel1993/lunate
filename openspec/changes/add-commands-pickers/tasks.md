# Tasks: Interactive session — commands, pickers, completion (T-22, part 2)

## 1. SelectList + live-area seam (Tui)

- [x] 1.1 `SelectListModel` + `SelectListRenderer` (title, `>` marker, current-item `*`, window of at most 8 items, deterministic golden)
- [x] 1.2 `LiveArea.SetPicker` + `PickerInput` (the approval pattern); render test
- [x] 1.3 `KeyRouter`: bare Tab → `RoutedKey.Complete`; Shift+Tab stays `Edit`; routing matrix updated

## 2. Completion helper (Tui)

- [x] 2.1 `Completion.Complete(text, candidates)` pure (one match → full; several → longest common prefix; no progress → candidate list); unit tests incl. synthetic colliding prefixes

## 3. Session listing (Agent)

- [x] 3.1 `Session.List(directory)` + `SessionSummary` (header-only read, newest modified first, id tie-break, cap 20, corrupt skip, never writes); tests; PublicAPI files updated

## 4. Commands + pickers (Coding)

- [x] 4.1 Command parsing + dispatch (skip pipeline, history, unknown-command notice); tests
- [x] 4.2 `/quit` (same path as the double Ctrl+C; works during a turn)
- [x] 4.3 `/compact` (idle guard; `CompactNowAsync`; true/false notices); tests
- [x] 4.4 Picker state machine (open/keys/confirm/dismiss; idle guard; approval exclusivity); tests
- [x] 4.5 Model picker + `/model <id>` (catalog; `AppendModelChange`; harness rebuild; footer; next request carries the history); tests
- [x] 4.6 `/new` (fresh session + rebuild + notice); tests
- [x] 4.7 `/resume` (`Session.List` + `Load` + rebuild + notice); tests
- [x] 4.8 Compose refactor (core + `BuildHarness(model, session)`) so rebuilds reuse it
- [x] 4.9 Tab-completion wiring (idle + scope checks; notice when no progress); tests

## 5. Steering echo (Coding)

- [x] 5.1 Pending FIFO + `HandleEvent(SteeringInjected)` → dim `» <text>` commit; tests (echoed on injection; returned leftovers never echoed)

## 6. Interactive entry (Coding)

- [x] 6.1 `Cli.Run` bare-`lunate` path + options seam + `ConsoleSupport.Check` gate + `-p` hint + exit 2; help text updated
- [x] 6.2 Tests (scripted console runs a session; non-terminal hint; help mentions the entry)

## 7. E2E + docs + gate

- [x] 7.1 Extend the scripted snapshot (echo, `/mod`+Tab → `/model`, picker selection, `/new`, `/resume` with id8 normalization, `/quit`); regenerate goldens deliberately
- [x] 7.2 `docs/guide.md`: key-binding table gains the `Tab` row
- [x] 7.3 `bash scripts/verify.sh` green; deviations recorded in design.md

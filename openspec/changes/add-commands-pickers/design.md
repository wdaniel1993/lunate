# Design: Interactive session — commands, pickers, completion (T-22, part 2)

## Structure

- `Lunate.Tui`: `SelectList.cs` (new: `SelectListModel` + `SelectListRenderer`, public primitives like `ApprovalPrompt*`), `LiveArea.SetPicker`/`PickerInput` (same pattern as `SetApproval`), `KeyRouter` gains `RoutedKey.Complete` (bare Tab; Shift+Tab stays `Edit`), `Completion.cs` (new, pure: `Completion.Complete(text, candidates)` → either a replacement or the candidate list to show).
- `Lunate.Agent`: `Session.List(directory)` (new, public) → `IReadOnlyList<SessionSummary>` (`Id`, `Path`, `Created`, `Modified`), reading each file's header line only; cap 20; newest modified first with the id as tie-break; unreadable headers skipped. PublicAPI files updated.
- `Lunate.Coding`: `Commands.cs` (parse + built-in table), command dispatch and the picker state machine in `InteractiveSession` (mirrors the approval pattern), `Cli.cs` interactive entry + help text.
- `docs/guide.md`: the key-binding table gains the `Tab` row.
- Tests: `Lunate.Tui.Tests` (SelectList render golden, KeyRouter Tab, Completion units), `Lunate.Agent.Tests` (`Session.List`), `Lunate.Coding.Tests` (per-command tests, picker tests, steering echo, interactive entry, extended E2E snapshot).

## Command dispatch (pinned)

- A submitted text whose first non-space character is `/` dispatches as a command: whitespace-split name + optional argument. It appends to the input history, **skips the input pipeline** (commands are frontend control, not model input — a hook must not rewrite `/model`), and never reaches the model. Unknown → dim notice `unknown command: /x`.
- Idle guard: `/new`, `/resume`, `/model`, `/compact` refuse with a notice while a turn runs (`a turn is running — Esc to cancel first`). `/quit` works at any time through the same quit path as the double Ctrl+C (it cancels a running turn and ends the session loop).
- `/compact`: idle only; `await _harness.CompactNowAsync()`; true → notice `context compacted` (an explicitly requested out-of-run compaction carries no `CompactionApplied` event per the agent-loop spec — the session notices itself); false → notice `nothing to compact`.
- `/model <id>`: resolved via the catalog; unknown → notice naming the id; no argument → open the model picker.
- While the approval prompt is open, input is blocked (existing rule), so no command can dispatch; while a picker is open, keys are intercepted by the picker.

## Pickers (pinned)

- `SelectListModel { Title, Items, Selected }` renders as a live-area block: title line, one line per item with a `>` marker and emphasis on the selected one, at most 8 visible items windowed around the selection. Deterministic golden at width 80.
- Keys while open: Up/Down (clamped, no wrap), Enter confirms, Esc dismisses; everything else is ignored. Pickers open only while idle; opening while busy → notice refusal.
- Model picker (Ctrl+L or `/model`): items = catalog models in catalog order, label `<id> (<provider>)`, the current one prefixed `* `. Selection → `Session.AppendModelChange(id)`, rebuild the harness against the **same** session (the harness restores history from `Session.HistoryItems()` at construction — existing behavior), update `_modelId` and the footer, notice `model: <id>`.
- Session picker (`/resume`): items = `Session.List(sessionDirectory)`; label = the session id's first 8 characters (`id8` — the E2E normalizes these via known-value replacement so the golden stays deterministic); the current session is included. Selection → `Session.Load(path)`, rebuild the harness, notice `resumed <id8>`. The resumed conversation continues on the **current** model (a session's last model change does not silently switch the model; noted as a follow-up).
- Rebuild: refactor `Compose()` into "compose core (settings/catalog/factory/workspace/tools) + build harness(model, session)" so `/model`, `/new` and `/resume` reuse it; rebuild only when idle.

## Steering echo (pinned)

- The session tracks pending steering texts FIFO (appended when steering is enqueued); `HandleEvent(SteeringInjected)` pops one and commits a dim `» <text>` block to scrollback. Leftovers returned to the input line (Esc / error / step limit) were never injected and are never echoed. The part-1 steering E2E flow gains the echo in its golden.

## Tab completion (pinned)

- Bare Tab routes to `RoutedKey.Complete`. The session applies `Completion.Complete` only when idle, no picker or approval is open, and the whole input is a single `/word` with the cursor at its end; otherwise no-op (v1 scope).
- Result: one match → the full command; several → the longest common prefix; no progress → dim notice `commands: /a /b …` (sorted). The pure helper takes the candidate list, so the common-prefix branch is unit-tested with synthetic candidates (the built-in set has no colliding prefixes today).
- `@path` completion stays T-53 (the helper grows a file index there).

## Interactive entry (pinned)

- `Cli.Run`: bare `lunate` (no arguments) → interactive mode. Options: `SystemConsoleIO` over the real console, `Scheduler = System.Reactive.Concurrency.Scheduler.Default`, all paths null (production defaults inside `InteractiveSession`). `ConsoleSupport.Check` non-null (or a non-terminal / `TERM=dumb` console) → stderr `lunate: interactive mode needs a terminal; use lunate -p "<prompt>"` + exit 2.
- Test seam: `Cli.Run` gains an optional `InteractiveSessionOptions?` parameter (mirroring `printOptions`); production builds defaults when null.
- `--help` documents the interactive entry and lists the commands on one line.
- The `Console.CancelKeyPress` handler stays; in raw mode Ctrl+C arrives as a key, not a signal.

## E2E snapshot (pinned)

Extend `A_scripted_session_matches_the_committed_scrollback_and_final_frame` (same golden files, regenerated deliberately with `LUNATE_CODING_UPDATE_GOLDENS=1`):

- The mid-run steer now also echoes `» steer mid-run` when injected (existing flow).
- After the existing flow: `/mod` + Tab completes to `/model`; Enter opens the model picker (golden shows the built-in catalog: `gpt-4o-mini`, `gpt-4o`, `claude-sonnet-5-5`, `claude-opus-5-5`); Down + Enter selects `gpt-4o` → notice + footer; `/new` → notice; `/resume` → picker lists the session directory (id8 labels normalized by the test: it scans the directory and replaces each known id8 with `<session>` before comparing); select the first session → notice; finally `/quit` ends the session (replacing the closing Ctrl+C).
- Determinism: every model call stays gated; picker navigation is scripted keys; waits use the deadline-bounded `WaitUntilAsync` + `Advance` pattern; no wall-clock reads.

## Deviations

Recorded during apply:

1. **E2E flow details**: the leftover `unsent steering` in the input is cleared with one Ctrl+C before `/mod` (typing would append to it); the picker renders are two dedicated goldens (`end-to-end-model-picker-frame.txt`, `end-to-end-session-picker-frame.txt`, the latter with both session labels normalized to `<session>`); the closing double-Ctrl+C quit is replaced by `/quit`, whose same-path quit is still pinned by the key tests.
2. **Interactive-entry hint**: the gate is `ConsoleSupport.Check`, but stderr always carries the pinned platform-independent hint (`lunate -p "<prompt>"`), not the Windows-specific check text; the non-terminal test therefore passes on every OS.
3. **Compaction tests**: the scripted test client gained a canned `GetResponseAsync` (the summarization call), and the "true" `/compact` test configures `CompactionKeepTurns = 1` (default 4) so scripted histories stay short.
4. **`ScriptedConsoleIO.IsInteractive` is settable** (default false unchanged) so the CLI-entry test can present a scripted terminal to the gate.
5. **Pending-steering FIFO is lock-guarded and leftovers are discarded from the front**: the key pump can enqueue concurrently with the run task's run-end drain, and the drained leftovers are always the pending FIFO's front segment.
6. **`Session.ListLimit` (20) is internal**; the public surface is only `Session.List` plus `SessionSummary`.
7. **`/model <id>` (and confirming the current model) always appends a model change and rebuilds**, even when the id is already current; no short-circuit was specified.
8. **Adversarial-fix round**: the session picker labels entries and the `resumed` notice with the session's full id (the `id8` helper is gone; the E2E maps the original session's id to `<old-session>` and the `/new`-created id to `<new-session>`, pinning two distinct entries with the new one first); "always" approvals are cleared whenever a rebuild starts a different session (`/new`, `/resume`); `Session.List` orders by last-modified, then creation time, then id — all descending.

## Part 3 / T-53 seams

- `@path` completion (T-53) extends `Completion` with a file index honoring `.gitignore`.
- Extension-declared commands (extensibility series) merge into the built-in table through the same dispatcher; the manifest already declares `commands`.
- Session picker previews (first-message excerpt) and restoring a resumed session's model are possible follow-ups.

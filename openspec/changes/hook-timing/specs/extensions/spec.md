# extensions — delta for hook-timing

## MODIFIED Requirements

### Requirement: Timeouts and continuation caps
Each handler SHALL run under a per-handler timeout (configurable; `ProviderStreamEvent` on a shorter fast path). Every dispatch SHALL be timed with a monotonic clock and aggregated per (extension, hook) for the session — call count, total, last and maximum milliseconds, and separate completed, timed-out and cancelled counters — thread-safely and without per-dispatch allocation, with child runs counted into the same session aggregate, and the elapsed time SHALL also be recorded to a `System.Diagnostics.Metrics` histogram on meter `Lunate.Extensibility` with `extension` and `hook` tags. A cancelled dispatch (user cancellation, run teardown) SHALL be counted as cancelled and SHALL NOT feed the maximum, the slow count or the warning — its elapsed time reflects the user; a timeout counts as the hook's cost. The first dispatch above the slow threshold (configurable, default 500 ms) SHALL log one warning naming the extension, the hook, the elapsed time and the threshold and pointing at `/extensions` — a dim notice in the interactive UI, log-only in headless and protocol modes, never stdout — and later crossings SHALL only increment the slow count. A remote extension's interceptor hook SHALL be timed at the same point, so its recorded time includes the child-process round trip; a remote observer hook goes out as a notification with no round trip, so its recorded time is the host-side send time, labeled as such, and the lazy start and `initialize` handshake SHALL be excluded from the first dispatch's timing, recorded separately as the extension's start duration. Continuations requested at turn boundaries SHALL be capped per run (default 3); requests beyond the cap SHALL be ignored and logged.

#### Scenario: A hanging handler is cut off by policy
- **GIVEN** a handler that never completes
- **WHEN** its timeout elapses
- **THEN** its hook's failure policy applies

#### Scenario: Continuation requests are capped
- **GIVEN** a run whose turn-boundary handlers keep requesting continuations
- **WHEN** the cap is reached
- **THEN** further requests are ignored and logged

#### Scenario: A slow hook warns once

- **GIVEN** a handler whose dispatches exceed the slow threshold repeatedly
- **WHEN** the hooks dispatch
- **THEN** one warning names the extension, the hook, the elapsed time and the threshold and points at `/extensions`, and the aggregate still counts every dispatch with its slow count

#### Scenario: Timings aggregate per extension and hook

- **GIVEN** two extensions handling the same hook and one of them also handling another
- **WHEN** the hooks dispatch
- **THEN** each (extension, hook) pair carries its own call count and maximum

#### Scenario: A cancelled dispatch is counted but never slow

- **GIVEN** a handler call cancelled by the user whose elapsed time exceeds the threshold
- **WHEN** the dispatch settles
- **THEN** the entry counts it as cancelled, the maximum and the slow count do not move, and no warning is logged

#### Scenario: Child runs land in the session aggregate

- **GIVEN** a subagent run dispatching a hook handled by an extension
- **WHEN** the run settles
- **THEN** the extension's hook entry counts the child run's dispatch

#### Scenario: The histogram records with extension and hook tags

- **GIVEN** a meter listener on `Lunate.Extensibility`
- **WHEN** a hook dispatches
- **THEN** a measurement arrives tagged with the extension id and the hook

#### Scenario: A cold remote start is not the first hook's time

- **GIVEN** a remote extension whose child starts on its first use
- **WHEN** its first interceptor dispatch is timed
- **THEN** the start and handshake are excluded from the hook time and reported as the extension's start duration

#### Scenario: A remote observer's time is the send time

- **GIVEN** a remote extension subscribed to an observer hook
- **WHEN** the hook dispatches
- **THEN** its recorded time is the host-side send time, with no round trip implied

The CLI SHALL provide an interactive `/extensions` command and a `lunate extensions [--json]` subcommand listing every discovered extension — failed first, then trust-denied, then loaded, each group by id — with its id, version, scope (`global`/`project`) and state (`loaded`, `trust-denied` or `failed`). Loaded extensions SHALL show their load time in milliseconds (a load above 500 ms marked `(slow)`) and their **registered** tool, command, hook and service counts, a field differing from the manifest's declared list carrying `(N declared)` — and, with at least one dispatch above the slow threshold, its slowest such hook as `hook <Hook> max <ms> ms, <slowCount>/<calls> slow`, and a remote extension its most recent start as `start <ms> ms`; trust-denied and failed extensions SHALL show the manifest's **declared** counts, and a failed extension's message SHALL be truncated to its first sentence followed by ` — see the log` when truncated. The interactive form SHALL render one segment per extension in the single-line notice discipline (segments joined `;`, prefixed `extensions: `); the subcommand SHALL print one line per extension with load times labelled `(CLI-measured)`, or with `--json` a machine-readable array of the same fields (`loadMs`, `slow` where applicable, `startMs` on a remote extension, and — when the session dispatched hooks — `hookTimings`, one `{hook, calls, totalMs, lastMs, maxMs, slowCount, timedOut, cancelled}` per timed hook), and SHALL compose the host in register-only inspect mode — loading and registering trusted extensions but starting no session events, background services or MCP servers, unloading each extension and stopping the host before exit — and SHALL never prompt, so an extension it cannot trust reads `trust-denied`. It SHALL exit non-zero when any extension failed. With no extensions discovered, the interactive form SHALL read `extensions: none configured` and `--json` SHALL print `[]`; with extensions disabled (`--no-extensions`) the interactive form SHALL read `extensions disabled (--no-extensions)`. The view SHALL never print settings values, secrets or environment values, and numbers SHALL be formatted culture-invariantly.

#### Scenario: A session shows each extension's scope, state, load time and contributions

- **GIVEN** a session with one loaded global extension and one project extension whose trust was denied
- **WHEN** `/extensions` is submitted
- **THEN** one segment per extension appears ordered failed, trust-denied, loaded — the loaded one showing `(global) loaded in N ms` and its registered counts, the denied one showing `(project) trust-denied` and its declared counts

#### Scenario: The subcommand is register-only and never prompts

- **GIVEN** a store with one trusted global extension whose background service and session hooks write markers, and one untrusted project extension
- **WHEN** `lunate extensions` runs non-interactively
- **THEN** the factory ran (registration marker present) but no service-start or session-hook marker exists, nothing prompted, no trust was recorded, the host stopped, and the exit code is 0; with `--json` the same data prints as an array of objects

#### Scenario: A failed extension makes the subcommand exit non-zero

- **GIVEN** a store with an extension whose load fails
- **WHEN** `lunate extensions` runs
- **THEN** its line reads `failed` with the first sentence of the message and ` — see the log` when truncated, and the exit code is non-zero

#### Scenario: The empty and disabled views

- **GIVEN** no extensions are discovered (or the run carries `--no-extensions`)
- **WHEN** `/extensions` is submitted (or `lunate extensions` runs)
- **THEN** the interactive form reads `extensions: none configured` (`extensions disabled (--no-extensions)` when disabled) and `--json` prints `[]`

#### Scenario: A session shows the slowest hook above the threshold

- **GIVEN** a session whose ToolCalling handler exceeded the slow threshold
- **WHEN** `/extensions` is submitted
- **THEN** the loaded extension's segment shows `hook ToolCalling max <ms> ms, <slow>/<calls> slow` and `--json` carries its `hookTimings` entry with the outcome counters

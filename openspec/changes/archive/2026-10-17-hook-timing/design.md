# Design — hook timing

## D1 — Measure at the one funnel, aggregate per (extension, hook)

Every dispatch site in `HookRunner` (observe loop, and the interceptor loops in `Tools`/`Context`/`Messages`) already funnels through `CallVoidAsync`/`CallAsync`, with the hook name and the owning `Registration` in scope. The timing wraps that funnel — one measurement point, no per-hook bookkeeping.

**Time source:** `Stopwatch.GetTimestamp`/`GetElapsedTime` (monotonic) — never `DateTime`.

**Aggregation is thread-safe and allocation-free on the hot path.** Subagents and parallel tool calls dispatch concurrently; the per-(extension, hook) aggregate (`calls`, `totalMs`, `lastMs`, `maxMs`, `slowCount`, `timedOut`, `cancelled`) updates with `Interlocked` operations on the entry's fields (or a lock per key — whichever stays allocation-free). Entries are created once per key and reused; the elapsed value stays a value type on the stack. A test pins that dispatch overhead with zero extensions is unchanged (no registrations → nothing measured, nothing allocated).

**Outcomes count separately: completed / timedOut / cancelled.** Cancelled dispatches (user Esc, run teardown) are counted in `cancelled` and excluded from `maxMs`, `slowCount` and the slow warning — their elapsed reflects the user, not the hook. Timeouts count toward slow like a completed call (they are the hook's cost). Every other field counts all dispatches.

**Child runs count into the session's aggregate** — the runner is session-wide, so subagent runs land in the same per-(extension, hook) entries. Stated in the spec.

**Metrics at the same point:** a `System.Diagnostics.Metrics` histogram on meter **`Lunate.Extensibility`** (unit ms) with tags `extension` and `hook`, recorded where the aggregate updates. It costs nothing without a listener, keeps the OTel story consistent, and carries no exporter work in this card. The tag array is built once per key (stored on the entry) so recording adds no per-call allocation.

Rejected: raw per-call lists (unbounded), sampling (misses the tail).

## D2 — Warn once per (extension, hook), threshold configurable, default 500 ms

The first dispatch crossing the threshold logs one warning naming **the extension, the hook, the elapsed time and the threshold**, and pointing to `/extensions` for the full picture. Later crossings increment `slowCount` only — a per-tool-call hook would otherwise spam the log. Threshold: `HookRunnerOptions.SlowHookThreshold`, default 500 ms — the same discipline as the load marker's 500 ms. The provider-stream fast path is measured like everything else; its 100 ms timeout means it effectively never crosses.

**Surfacing discipline:** the warning goes through the extension log, and the runner additionally raises a slow-hook event. The interactive session subscribes and renders it as one dim notice; with no subscriber (headless, print, ACP, `--json`, the CLI) the warning stays in the log only and never reaches stdout. The event is the mechanism because the interactive default extension log is `StderrExtensionLog` — a log-only sink — so a log line alone would be invisible in the TUI.

Rejected: warn on every crossing (log spam); a hard-coded const (every other runner knob is an option).

## D3 — Surface in `/extensions` and its `--json`, not `/context`

Hooks already show in the `/extensions` view (registered counts per extension). A loaded segment whose hooks crossed the threshold appends its slowest hook unambiguously: `hook ToolCalling max 812 ms, 3/120 slow` (`slowCount`/`calls` — only when something crossed, so quiet extensions stay one line). A remote extension's segment also carries its most recent start duration: `start 640 ms`.

`--json` gains `hookTimings` (one entry per timed hook: `hook`, `calls`, `totalMs`, `lastMs`, `maxMs`, `slowCount`, `timedOut`, `cancelled`) on loaded extensions, and `startMs` on remote extensions. The CLI `lunate extensions` runs in register-only inspect mode — no session, no dispatches — so it has no `hookTimings` and its output is otherwise unchanged; `/context` stays about the context budget.

## D4 — Remote extensions: round trip for interceptors, send time for observers, preparation excluded

The remote adapter registers ordinary handlers; the measurement wraps the handler call. Refinements:

- **A remote interceptor hook's time includes the child-process round trip** — that is a contract, stated in the spec.
- **Observer hooks that go out as JSON-RPC notifications have no round trip to time.** Their recorded time is the host-side send time, labeled as such in the spec and docs next to the round-trip contract — fire-and-forget means the child's work is not measurable, and pretending otherwise would be dishonest.
- **The lazy start + `initialize` handshake are excluded from the first hook call's timing.** A cold remote extension would otherwise warn once for nothing. The extension measures its start+handshake separately and reports it (`startMs` on its segment and in `--json`); the runner subtracts a reported preparation time from that dispatch's measured elapsed through a small optional interface on the handler (the remote hook wrapper implements it; a future preparable handler can too). Preparation never feeds `maxMs`, `slowCount` or the warning.

## D5 — Numbers are culture-invariant

Milliseconds format with `CultureInfo.InvariantCulture` like the load time; the `(slow)` marker and threshold comparison use the raw `double`, never formatted text.

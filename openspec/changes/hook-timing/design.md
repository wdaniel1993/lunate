# Design — hook timing

## D1 — Measure at the one funnel, aggregate per (extension, hook)

Every dispatch site in `HookRunner` (observe loop, and the interceptor loops in `Tools`/`Context`/`Messages`) already funnels through `CallVoidAsync`/`CallAsync`, with the hook name and the owning `Registration` in scope. The timing wraps that funnel — one measurement point, no per-hook bookkeeping.

The session's aggregate is in memory on the runner: one entry per (extension id, hook) with `calls`, `totalMs`, `lastMs`, `maxMs`, exposed as a snapshot (`HookTimings`). Bounded by (extensions × hooks) — small. No persistence; the runner is per session, so the data resets with the session by construction. A timed-out or cancelled dispatch counts (its elapsed time is real and informative).

Rejected: raw per-call lists (unbounded), sampling (misses the tail).

## D2 — Warn once per (extension, hook), threshold configurable, default 500 ms

The first dispatch crossing the threshold logs one warning naming the extension, the hook and the elapsed time. Later crossings are counted in the aggregate, not logged again — a per-tool-call hook would otherwise spam the log on every call. Threshold: `HookRunnerOptions.SlowHookThreshold`, default 500 ms — the same discipline as the load marker's 500 ms. The provider-stream fast path is measured like everything else; its 100 ms timeout means it effectively never crosses.

Rejected: warn on every crossing (log spam); a hard-coded const (every other runner knob is an option).

## D3 — Surface in `/extensions` and its `--json`, not `/context`

Hooks already show in the `/extensions` view (registered counts per extension). The loaded segment appends its slowest over-threshold hook as `hook <Hook> <ms> ms (slow)` — only when something crossed, so quiet extensions stay one line. `--json` gains `hookTimings` (one `{hook, calls, totalMs, lastMs, maxMs, slow}` per dispatched hook) on loaded extensions. The CLI `lunate extensions` runs in register-only inspect mode — no session, no dispatches — so it has no `hookTimings` and its output is unchanged; `/context` stays about the context budget.

## D4 — Remote extensions: the same point, the full round trip

The remote adapter registers ordinary handlers; the measurement wraps the handler call, so a remote extension's recorded time includes the stdio round trip and child execution by construction. The spec states this so it is a contract, not a coincidence.

## D5 — Numbers are culture-invariant

Milliseconds format with `CultureInfo.InvariantCulture` like the load time; the `(slow)` marker and threshold comparison use the raw `double`, never formatted text.

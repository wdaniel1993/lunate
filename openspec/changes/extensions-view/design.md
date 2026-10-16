# Design — extensions-view

## D1 — Data model: per-extension load results, recorded at compose

Today the session only keeps `ExtensionSessionError(id, message)` for failures; "loaded" is
implicit and load time is not measured. `ExtensionSession` gains
`IReadOnlyList<ExtensionLoadResult>` — one entry per discovered descriptor: `(Id, State, Elapsed,
Message?)` with `State ∈ { Loaded, TrustDenied, Failed }`. `StartAsync` wraps each
`loader.Load(...)` in a `Stopwatch`; `ExtensionTrustDeniedException` → `TrustDenied`,
`ExtensionLoadException` → `Failed`, success → `Loaded` (elapsed recorded for all three; shown
only for loaded). The existing `Errors` list stays as-is (logging + id-less discovery errors) —
no consumer changes.

## D2 — Order, format, thresholds (pinned; culture-invariant)

**Order**: state groups `failed` → `trust-denied` → `loaded`, then by id (`Ordinal`) — problems
first. One segment per extension, joined `; `, prefixed `extensions: ` — the `/mcp`//`/context`
single-line notice discipline:

- loaded: `ext-one 0.1.0 (global) loaded in 12 ms, tools 2, commands 0, hooks 1, services 1`
  — counts are **registered** counts (actual); a field whose count differs from the manifest's
  declared list carries `(N declared)` (T-47 D6: registered-but-undeclared is refused at load,
  declared-but-not-registered warns and is dropped, so the mismatch direction is declared >
  registered). Load time above **500 ms** carries `(slow)`.
- trust-denied / failed: `ext-two 0.2.0 (project) trust-denied, tools 1, commands 0, hooks 0,
  services 1` — counts are the manifest's **declared** lists (nothing loaded).
- failed: `failed: {first sentence} — see the log` (first sentence = up to and including its
  first `.`; the ` — see the log` pointer appears when the message was truncated).
- empty: `extensions: none configured`; `--no-extensions`: `extensions disabled
  (--no-extensions)`.
- **Slow threshold: 500 ms per extension** — pinned here because the spec's "warn above a
  threshold" had no number; a healthy extension loads in single-digit-to-tens of ms and the
  whole 10-extension discovery budget is 250 ms, so 500 ms is clearly pathological. The formatter
  owns the constant.

Milliseconds via `CultureInfo.InvariantCulture`. Discovery errors are not part of the view (they
stay on the host log). The view never prints settings values, secrets or environment values.

## D3 — CLI: `lunate extensions [--json]`

Mirrors `lunate trust`'s shape (routing in `Cli.cs`, `Run(args, workingDirectory, output, errors,
seams)`). **Register-only inspect mode**: it composes the host like a session start but with
`ExtensionSeams.Inspect = true` → `StartAsync` passes `startSession: false` to `Load`, which skips
`StartSessionAsync` (no session events), the background-service start, and the `ToolsChanged`
hook. Factories run and register; nothing starts. The default non-interactive prompt applies —
never interactive, so an untrusted project extension reads `trust-denied`. After rendering, each
loaded extension is unloaded (releasing its collectible context) and the host disposed. **Exit
code**: 1 when any extension is `Failed`, else 0 (denied is expected without trust — not an
error). Text output: one line per extension, load times labelled `(CLI-measured)` (they are this
run's measurements, not a session's); `--json`: array of `{id, version, scope, state, loadMs?,
slow?, tools, commands, hooks, services}`; `--no-extensions` → the disabled line / `[]`.

Devil's advocate — why not discovery-only (no loading)? The promise includes state and load time;
those exist only after composing. Register-only is exactly the load path minus side effects: no
hooks, no services, no MCP — and it can never prompt or load anything the trust store has not
approved. The marker test proves the restraint.

## D4 — ACP

ACP has no slash-command surface, so there is nothing to wire; the host log already carries the
per-extension load lines. Noted so the gap card's "ACP logs" clause is satisfied by the existing
behavior.

## D5 — Rendering seam

A small `ExtensionsView` formatter (static, in `Lunate.Coding`) builds the ordered segment list
from descriptors + `ExtensionLoadResult`s + registered counts; the interactive command wraps it in
the notice prefix, the CLI prints one line per extension and owns the JSON shape. One
implementation, two call sites — no duplicated formatting. Registered counts come from
`ExtensionToolRegistry.List()` (per extension), plus new count accessors on `HookRunner` and
`ServiceRegistry`; registered commands are always 0 (no registration API exists — declared-only).

## D6 — Sign-off (Daniel, 2026-10-16)

1. Single-line segments ✓; **sort by state (failed, trust-denied, loaded) then id; failed messages
   truncated to one sentence + log pointer**.
2. CLI ✓ with pins: **register-only — no session events, background services or MCP starts; then
   unload; marker test; non-zero exit on any failed; load time labelled CLI-measured**.
3. **Loaded shows registered counts with a mismatch note vs the manifest (T-47 D6); denied/failed
   show declared counts; `(slow)` marker above the threshold; empty `extensions: none configured`;
   `--no-extensions` → `extensions disabled (--no-extensions)`**. Per-hook timing deferred to a
   follow-up card.

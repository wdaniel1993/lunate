# Design — extensions-view

## D1 — Data model: per-extension load results, recorded at compose

Today the session only keeps `ExtensionSessionError(id, message)` for failures; "loaded" is
implicit and load time is not measured. `ExtensionSession` gains
`IReadOnlyList<ExtensionLoadResult>` — one entry per discovered descriptor in discovery order:
`(Id, State, Elapsed, Message?)` with `State ∈ { Loaded, TrustDenied, Failed }`. `StartAsync`
wraps each `loader.Load(...)` in a `Stopwatch`; `ExtensionTrustDeniedException` → `TrustDenied`,
`ExtensionLoadException` → `Failed`, success → `Loaded` (elapsed recorded for all three; shown
only for loaded). The existing `Errors` list stays as-is (logging + id-less discovery errors) —
no consumer changes.

## D2 — Format (pinned; culture-invariant)

One segment per extension, joined `; `, prefixed `extensions: ` — the `/mcp`//`/context`
single-line notice discipline:

- loaded: `ext-one 0.1.0 (global) loaded in 12 ms, tools 2, commands 0, hooks 1, services 1`
- trust-denied: `ext-two 0.2.0 (project) trust-denied`
- failed: `ext-broken 0.1.0 (project) failed: {message}`
- empty: `extensions: none configured`

Counts come from the manifest's declared lists (`Tools`, `Commands`, `Hooks`, `Services`) — they
are declarations, not runtime registration counts. Milliseconds via `CultureInfo.InvariantCulture`.
Discovery order = the loader's descriptor order (global scope before project scope). Discovery
errors are not part of the view (they stay on the host log). The view never prints settings
values, secrets or environment values.

## D3 — CLI: `lunate extensions [--json]`

Mirrors `lunate trust`'s shape (routing in `Cli.cs`, `Run(args, workingDirectory, output, errors,
seams)`). It composes an `ExtensionSession` like any session start (non-interactive default
prompt — never interactive, so an untrusted project extension reads `trust-denied`), renders the
same fields as one line per extension (or a JSON array of `{id, version, scope, state, loadMs?,
tools, commands, hooks, services}`), then stops the host (`DisposeAsync` → `EndSessionAsync`)
before exit. Exit 0 with extensions, 0 with none (`extensions: none configured` / `[]`).

Devil's advocate — why not discovery-only (no loading)? The promise includes state and load time;
those exist only after composing. Composing non-interactively is exactly what a print/ACP session
start already does, so the subcommand adds no new trust surface (it can never prompt and never
loads anything the trust store has not approved).

## D4 — ACP

ACP has no slash-command surface, so there is nothing to wire; the host log already carries the
per-extension load lines. Noted so the gap card's "ACP logs" clause is satisfied by the existing
behavior.

## D5 — Rendering seam

A small `ExtensionsView` formatter (static, in `Lunate.Coding`) builds the segment list from
`IReadOnlyList<ExtensionLoadResult>` + descriptors; the interactive command wraps it in the
notice prefix, the CLI prints one line per extension and owns the JSON shape. One implementation,
two call sites — no duplicated formatting.

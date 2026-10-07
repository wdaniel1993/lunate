# add-sample-memory-provider

## Why

Card T-44 (deps T-39, T-23): the second fitness sample. Fitness row: "memory-provider | ContextBuilding injection, TurnEnded storage via a registered service; survives compaction by re-injection". It proves the persistence pattern the extension model is built around — an extension that remembers things across a session, injects them before every model request, and whose injected context **survives compaction** because re-injection happens at request-build time, not in the session history. No spec deltas: every primitive exists (ContextBuilding, MessageCompleted, TurnEnded, registered services, source-tagged context, compaction since T-23) — the sample proves them together (`skip_specs`).

## What Changes

- **Sample `samples/extensions/memory-provider/`** (abstractions-only): a memory store as a **registered service** (`memory-store`; in-memory per session, documented as the sample's store — production extensions bring their own persistence), plus handlers: `MessageCompleted` (user role) captures lines marked `remember: …` into the service's buffer; `TurnEnded` commits the buffer through the service and appends an audit extension entry; `ContextBuilding` injects a source-tagged memory section before every request.
- **Kit-based tests** proving: capture → store via the service; injection in the next request; **re-injection after compaction** (a session long enough to compact — memory section present in the post-compaction request while history was replaced); service lifecycle (started/stopped once); over-budget additions dropped + logged (per-extension budget).
- README: capabilities proven; note that the sample store is in-memory (persistence is the extension's own concern; a per-extension data directory is a candidate future primitive).

## Impact

- New `samples/extensions/memory-provider/` (extension + tests) in the solution; layering edges in the gate.
- No core changes; no new packages; no golden changes; no new ADR.

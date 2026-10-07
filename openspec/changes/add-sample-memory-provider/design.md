# Design: add-sample-memory-provider

## Context

Fitness row: "memory-provider | ContextBuilding injection, TurnEnded storage via a registered service; survives compaction by re-injection". All primitives exist: `ContextBuilding` (added, source-tagged, budgeted messages), `MessageCompleted` (role + text), `TurnEnded` (extension entries + continuation), registered services with session lifecycle (T-39), compaction with per-request context building (T-23). The sample must not need a workaround; nothing new is built. No new packages; no ADR; `skip_specs`.

## Sample: `samples/extensions/memory-provider/`

- **Manifest**: id `memory-provider`, apiVersion `^1.2.0` (uses service registration from 1.2), hooks `[message-completed, turn-ended, context-building]`, services `[memory-store]`, settings schema: `maxMemories` (number, default 20).
- **Service** (`MemoryStore : IBackgroundService`): ordered list of memories (text + timestamp); `Add`, `Commit` (buffer → list, bounded by `maxMemories`, oldest dropped first); `StartAsync`/`StopAsync` track lifecycle counters; the store is in-memory per session — documented as the sample's choice (production extensions own their persistence).
- **Handlers**:
  - `MessageCompleted` (user role only): lines starting `remember: ` (case-insensitive, trimmed) go into the buffer; nothing else is stored (deterministic, testable; no heuristics).
  - `TurnEnded`: `store.Commit()` — storage happens at the turn boundary through the registered service (per the fitness row); the result also appends one extension entry (`memory-provider/committed` with the committed count) so the session carries an audit trail.
  - `ContextBuilding`: when the store has memories, add ONE source-tagged message (`## Memory` heading + one bullet per memory, capped at `maxMemories`); the runner's per-extension budget applies (over-budget additions dropped + logged — exercised by a test).
- **Tests** (kit + replay fixtures):
  1. `remember: X` in a user message → after the turn, the service holds X (via log/state probe through the kit).
  2. The next request contains the memory section with X (ContextBuilding injection, source-tagged).
  3. **Re-injection after compaction**: a long recorded session crosses the compaction threshold (T-23 machinery); assert the post-compaction request still contains the memory section (and that the compaction summary is present) — the memory survives because injection is per-request.
  4. Service lifecycle: started and stopped exactly once across the session.
  5. Budget: an over-budget memory addition is dropped and logged with the extension id.
- Fixture: recorded via the temporary `RecordingChatClient` pattern (long session incl. `remember:` markers + compaction); committed; byte round-trip covered by `CommittedFixtureTests`.
- **README**: capabilities proven (ContextBuilding, MessageCompleted, TurnEnded storage via a registered service, source-tagged context, per-extension budget, compaction re-injection, kit) + the in-memory-store note.
- **Wiring**: both projects into the solution; `LayeringChecker`: `MemoryProviderExtension -> Lunate.Extensibility.Abstractions` only; `MemoryProviderExtension.Tests -> {Testing, Abstractions, Ai}`.

## Testing strategy

- Replay determinism: fixtures snapshot-stable; no network/home; both cultures.
- The compaction-crossing test reuses the harness/kit wiring from T-23's fixture approach; if composing memory markers + compaction in one fixture proves brittle, split into two fixtures (capture+inject; compaction survival) — either is acceptable, both must be recorded.

## Out of scope

Persistence to disk (extension-owned; candidate future primitive: a per-extension data directory), semantic retrieval/embeddings, UI surfacing, other samples (T-45+).

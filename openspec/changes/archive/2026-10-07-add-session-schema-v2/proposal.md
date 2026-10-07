# add-session-schema-v2

## Why

Sessions are the durable record of agent work, and Part A of the extensibility architecture must land before frontends and stored sessions multiply (ADR-0017, `docs/spec/extensibility.md`). Schema 1 (T-11) covers messages, compaction and model changes; it cannot yet record extension-owned entries, repository/worktree identity, nested tool calls, or content the core does not understand — all of which the extensibility architecture and the worktree addendum require.

## What Changes

- **Schema 2**: `SessionFormat.SchemaVersion` bumps to 2; the loader accepts 1 and 2 — v1 sessions load unchanged (migration is read-side; no rewrite).
- **Header**: gains optional `repo` (repository identity) and `worktree` (worktree root path) fields. Session locations group by repository identity with one folder per worktree; outside a repository the current per-cwd grouping stays. (Per the worktree addendum's landing map.)
- **New core entries**: `activeTools` (active-tool set change), `promptSection` (system-prompt-section change, for deterministic replay), `childSession` (subagent link), `nestedCalls` (bounded record on tool results: name, args, status, duration — never results; explicit size caps).
- **Extension entries**: `ext/<extension-id>/<type>` with an opaque payload preserved verbatim (raw JSON text) across load and rewrite.
- **Unknown content preservation**: when a loaded message contains content the core cannot re-serialize byte-identically (provider-hosted tools), the original node is kept and re-emitted unchanged.
- **Goldens**: new v2 fixtures (header with repo/worktree, each new entry, ext entry, unknown-content round-trip) under **ADR-0018** (AGENTS.md requires an ADR for golden changes); v1 fixtures stay as load-compatibility goldens.

## Impact

- `Lunate.Agent`: `SessionEntry`, `SessionFormat`, `Session`, `SessionPaths` (+ tests and goldens).
- Harness wiring for `nestedCalls` only — the data exists since `add-extension-formats`; the other new entries are format + API and get wired by their producing cards (active tools: T-36; prompt sections: prompt assembly; child sessions: the subagent cards).
- Docs: `agent-sessions` spec deltas; ADR-0018 (root `adr/`).

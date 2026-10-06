## 1. Types and store

- [x] 1.1 `SessionEntry` hierarchy: header (schema, cwd, created, meai), message (embedded ChatMessage, optional model/usage), compaction (summary, replaces), modelChange (model) — sealed records, UTC timestamps
- [x] 1.2 `SessionFormat` (internal): deterministic serialize/parse per the design; message node via `AIJsonUtilities.DefaultOptions`
- [x] 1.3 `Session`: create (writes header), append helpers (store-assigned ids/parent chain), load (schema validation with actionable errors), `Entries`, `ToHistory()`
- [x] 1.4 `SessionPaths.ForProject(cwd)` → `~/.lunate/sessions/<hash8>/`; file name `<sessionId>.jsonl`

## 2. Harness integration

- [x] 2.1 `AgentHarnessOptions.Session`; constructor seeds history from `ToHistory()`
- [x] 2.2 One append helper for every history mutation (user, assistant with model/usage, tool results incl. repaired) that mirrors into the session

## 3. Golden files and tests

- [x] 3.1 `tests/fixtures/sessions/`: `golden-text.jsonl`, `golden-tool-call.jsonl`, `golden-mixed.jsonl` (compaction + modelChange shapes included)
- [x] 3.2 Format guard test: every golden deserializes and re-serializes byte for byte; parse errors are actionable
- [x] 3.3 Store tests: create/append/load round trip; id and parent chain; schema mismatch fails actionably; `ToHistory()` order; `SessionPaths` hash stability
- [x] 3.4 Harness tests: a scripted run with a session writes the expected entries (byte-stable snapshot); a resumed session's first request contains the loaded history (scripted client assertion)

## 4. Specs and ADR

- [ ] 4.1 New `agent-sessions` capability spec (format, store/resume, guard, location) with scenarios
- [ ] 4.2 `agent-loop` delta: session mirroring + resume seeding
- [ ] 4.3 ADR-0015 (proposed): the format is pinned by golden files; schema bumps need migration + ADR — and copy it to `adr/0015-session-format.md` at the repository root (the change-dir copy stays as the proposal record)

## 5. Close

- [ ] 5.1 `scripts/verify.sh` green; `openspec validate add-session-store --type change --strict`; self-review; commit per group

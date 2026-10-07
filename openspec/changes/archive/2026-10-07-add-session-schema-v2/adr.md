# ADR-0018: Session schema 2

- Status: proposed — 2026-10-07
- Context: `add-session-schema-v2` (guide: Part A of the extensibility architecture)

## Context

Schema 1 (ADR-0015, `add-session-store`) is frozen by golden tests. The extensibility architecture (ADR-0017) and the worktree addendum require sessions to record more than messages: extension-owned entries, repository/worktree identity, nested tool calls, and content the core does not understand. These must land before frontends and stored sessions multiply.

## Decision

- **Schema 2.** `SessionFormat.SchemaVersion` is 2; new files write `"schema":2`. The loader accepts 1 and 2; migration is read-side (v1 semantics are a subset of v2). Mixed-version files (v1 header, later v2 lines) are legal: the header records the creating version.
- **Header.** `SessionHeaderEntry` gains optional `repo` (repository identity string — normalized `GitCommonDir` or remote URL, chosen by the caller) and `worktree` (worktree root path) fields, omitted when null. The session layer never runs git itself.
- **Grouping.** `SessionPaths.ForRepository(repoIdentity, worktreePath)` groups sessions by repository with one folder per worktree (`<hash8(repo)>/<hash8(worktree)>/`); the existing per-cwd grouping remains the non-repository fallback.
- **New core entries.** `activeTools`, `promptSection`, `childSession`, `nestedCalls` — the last bounded by contract (name, capped arguments, status, duration; at most 32 calls; arguments truncated at 200 characters; never results).
- **Extension entries.** Namespaced `ext/<extension-id>/<type>` with an opaque payload preserved as raw JSON text and re-emitted byte for byte (`WriteRawValue`); loading takes `GetRawText()` as the source of truth.
- **Unknown content.** A loaded message that cannot be re-serialized deep-equal to its original node keeps the original raw JSON and re-emits it unchanged. An unrecognized `$type` discriminator fails the load with an error naming the file and the line; preserving those is deferred (placeholder-message design).
- **Goldens.** Golden files change under this ADR (AGENTS.md requirement): new v2 fixtures are added; every v1 fixture keeps its exact bytes as a load-compatibility golden.

## Consequences

- The line writer moves to a `Utf8JsonWriter` path so raw-preserved parts embed exactly; the existing goldens guard that schema 1 output is unchanged.
- Extension payloads and unknown content are stable across rewrites without the core understanding them.
- Load performs one extra serialization per message for the deep-equal check (bounded, covered by the startup budget).
- Producing cards wire the remaining entries: `activeTools`/`promptSection` with prompt assembly and T-36; `childSession` with the subagent cards; `nestedCalls` is wired now (data exists since `add-extension-formats`).

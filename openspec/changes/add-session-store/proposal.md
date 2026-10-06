## Why

Card T-11: sessions are the loop's durable memory and the foundation for resume, compaction and print mode. The guide fixes the format (append-only JSONL, one entry per line, `ChatMessage` embedded exactly as `AIJsonUtilities` serializes it) and the guard (golden files that must round-trip byte for byte, with a schema bump + migration + ADR when a Microsoft.Extensions.AI update changes the output). Nothing exists yet: the harness keeps history in memory only.

## What Changes

- **Session store** (`Lunate.Agent`): `Session` with create / append / load; entry ids and parent chain assigned by the store; entries for messages (with model and usage when known), compaction and model changes; `ToHistory()` reconstructs the message list for resume.
- **Harness integration**: `AgentHarnessOptions.Session` — a resumed session seeds the history, and every message the loop appends (user, assistant, tool results including repaired ones) is appended to the session.
- **Format guard**: golden session files in `tests/fixtures/sessions/` (text, tool-call, mixed) that deserialize and re-serialize byte for byte; a schema-mismatch load fails actionably.
- **Location**: `~/.lunate/sessions/<project-hash>/` helper (`SessionPaths`), used by the CLI later.
- **ADR-0015**: the session format is pinned by golden files; schema bumps require a migration and an ADR.
- Out of scope: compaction writing entries (T-23), `--continue`/`--resume` CLI flags and pickers (T-17/T-22), model-change writing (T-16).

## Capabilities

### New Capabilities
- `agent-sessions`: the session file format, the store and resume semantics, and the byte-for-byte format guard.

### Modified Capabilities
- `agent-loop`: with a session attached, the loop mirrors every history append into the session; a resumed session seeds the history.

## Impact

- New in `src/Lunate.Agent/`: `Session.cs`, `SessionEntry.cs`, `SessionFormat.cs`, `SessionPaths.cs` (+ PublicAPI entries). `AgentHarness.cs`/`AgentHarness.Tools.cs`/options: session hooks. New `tests/fixtures/sessions/` goldens + `Lunate.Agent.Tests` coverage. No new packages.

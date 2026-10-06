## Context

The guide fixes the session shape (entries with `id`, `parentId`, `type`, UTC timestamp; `ChatMessage` embedded as `AIJsonUtilities` serializes it) and the guard (golden files, byte-for-byte; schema bump + migration + ADR on MEAI drift). The loop currently keeps history in memory; T-11 adds the durable store and resume, wiring compaction (T-23) and CLI flags (T-17/T-22) later.

## Goals / Non-Goals

**Goals:** a store whose serialized output is frozen by golden tests; resume that reconstructs exactly the message history; harness integration that cannot diverge history and session (one append path).

**Non-Goals:** compaction entries (T-23), model-change entries (T-16), CLI flags/pickers (T-17/T-22), session pruning/rotation, encryption.

## Decisions

- **Envelope + embedded message**: each line is `{type, id, parentId, timestamp, …}` with the `ChatMessage` as a JSON node produced by `AIJsonUtilities.DefaultOptions`; the envelope is serialized with controlled property order so output is deterministic. Header line: `{type:"header", schema:1, id, cwd, created, meai}` where `meai` is the informational version of the Microsoft.Extensions.AI.Abstractions assembly (recorded, not validated).
- **Ids**: session id `s_<utc timestamp>-<4 hex>` (unique without coordination, like recordings); entry ids `e_01`, `e_02`, … (≥2 digits, no re-padding at 100); `parentId` = previous entry's id (linear chain), `null` for the first message entry; the store assigns id/parent/timestamp so callers pass payloads only.
- **Timestamps**: `DateTimeOffset` serialized as `O` in UTC (the `FixtureFormat` precedent).
- **Store API**: `Session.Create(path, cwd)` writes the header; `AppendMessage(ChatMessage, model?, usage?)`, `AppendModelChange`, `AppendCompaction` append lines; `Session.Load(path)` validates the header schema (actionable error naming schema + file) and parses entries; `ToHistory()` returns messages in order (compaction-aware request building is T-23's refinement — no compaction entries exist until then).
- **Harness integration**: `AgentHarnessOptions.Session`; the constructor seeds `_history` from `ToHistory()`; all history mutations go through one helper that also appends the message entry (model from the last non-null `ChatResponseUpdate.ModelId`, the last `UsageDetails` seen for the assistant entry). Repaired synthetic results are appended too — they are part of the history the model saw.
- **Format guard**: three goldens (text, tool-call, mixed with compaction/modelChange shapes) hand-authored to the spec; a test deserializes and re-serializes each and compares byte for byte; the schema-bump/migration/ADR policy goes to ADR-0015.
- **Location**: `SessionPaths.ForProject(cwd)` = `~/.lunate/sessions/<sha256(normalized cwd) first 8 hex>/`; session files `<sessionId>.jsonl`.

## Risks / Trade-offs

- [MEAI serialization drift breaks goldens] → that is the guard working: bump schema, add migration, record an ADR (ADR-0015).
- [Envelope property order changes silently] → byte-for-byte goldens freeze it; any change fails the test.
- [Session in options feels odd (stateful in a record)] → it is pragmatic and explicit; T-22 constructs one harness per session.

## Migration Plan

Not applicable — first format version; schema 1.

## Open Questions

- None blocking.

## Context

Guide Layer 1 ("Recorded streams", "What Lunate.Ai contains") defines the shape: `RecordingChatClient`/`ReplayChatClient`, JSONL under `tests/fixtures/streams/`, header line with model id + request hash, `AIJsonUtilities` serialization, contract tests only under `LUNATE_LIVE=1`. This change fixes the exact file format and matching rules so later cards can rely on them.

## Goals / Non-Goals

**Goals:** a deterministic record→replay pair with an exact, versioned fixture format; factory wiring under `LUNATE_RECORD=1`; a committed fixture that proves replay without keys.

**Non-Goals:** real-provider recordings (maintainer task, fixtures consumed from T-06 on); `StreamAccumulator` (T-06); session JSONL (Layer 2); retries.

## Decisions

- **Fixture format (schema 1)**: JSONL, one JSON object per line.
  - Line 1: `{"type":"header","schema":1,"model":"<id>","recordedAt":"<ISO-8601>"}`.
  - Then one line per exchange: `{"type":"exchange","requestDigest":"<sha256-hex>","updates":[...]}` — `updates` holds the full `ChatResponseUpdate` sequence in order, serialized with `AIJsonUtilities.DefaultOptions`.
  - Unknown `type` or `schema` ≠ 1 → actionable error naming the file (forward-compat guard).
- **Request digest**: SHA-256 over the `AIJsonUtilities` serialization of the request messages plus the response-affecting options (model id, tool names). The exact input set lives in one helper and is covered by a test — changing it is a schema event.
- **Replay matching**: strictly sequential; each incoming request must match the next exchange's digest (order + digest), otherwise `InvalidOperationException` naming fixture, exchange index and both digests (truncated). Exhaustion produces a "fixture exhausted" error. `GetResponseAsync` aggregates the same replayed updates so both call styles are consistent.
- **Recorder**: `DelegatingChatClient` that tees the stream through unchanged while buffering updates per request; on stream completion it appends the exchange line (header on file creation, directory creation, single lock; append-per-exchange keeps a crashed run's earlier exchanges usable).
- **Factory wiring**: `LUNATE_RECORD` truthy → default `recorderDecorator` = `inner => new RecordingChatClient(inner, path)`; explicit decorator argument still wins (tests). Path: `LUNATE_RECORD_PATH` else `artifacts/recordings/<yyyyMMdd-HHmmss>.jsonl`; default path is gitignored. Committed fixtures live in `tests/fixtures/streams/`.
- **Serialization round-trip guard**: serialize → deserialize → re-serialize must be byte-identical (same discipline as the session format guard), so MEAI updates that change serialization fail loudly.
- **Tests**: scripted provider via the existing `providerClientFactory` seam (no network); record→replay identity through the full factory pipeline (telemetry + logging active); digest mismatch, exhaustion, header/schema errors; replay twice is identical.

## Risks / Trade-offs

- [MEAI type serialization gaps] → round-trip guard test; if a consumed type lacks converters, project only what the loop consumes and record that decision here.
  - **Resolved during apply (schema 1):** `AIJsonUtilities.DefaultOptions` resolves types through a source-generated context that rejects unannotated roots (`ChatResponseUpdate[]`, wrapper records). No projection was needed: the fixture serializer clones the MEAI options and swaps in `DefaultJsonTypeInfoResolver`; MEAI's converters still own the wire shape (`$type: text` / `functionCall` / `functionResult` / `usage` / `error`, camelCase, declaration order). The round-trip guard pins all of it.
- [Digest brittleness] → digest inputs are one helper + one test; changes are explicit schema events, never silent.
  - **Frozen during apply:** request digest = SHA-256 of `{"messages":[...],"modelId":...,"toolNames":[...]}` serialized with the fixture options; the exact value is asserted by `Request_digest_matches_the_frozen_schema_value`.

## Migration Plan

Not applicable — additive.

## Open Questions

- None blocking.

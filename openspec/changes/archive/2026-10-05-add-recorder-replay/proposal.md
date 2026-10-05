## Why

T-05 fills the recorder/replay slot T-04 left open: recording turns provider streams into committed JSONL fixtures, and replay plays them back so every later card (StreamAccumulator, the loop, the TUI, evals) runs deterministic tests without API keys or network. Guide principle 6 ("everything is replayable") rests on this pair.

## What Changes

- `RecordingChatClient` (a `DelegatingChatClient`) tees every `ChatResponseUpdate` to a JSONL fixture — a header line (schema, model id, timestamp) plus one entry per exchange (request digest + update stream), serialized with `AIJsonUtilities` options. Active when `LUNATE_RECORD=1`; path from `LUNATE_RECORD_PATH` (default under `artifacts/recordings/`, gitignored).
- `ReplayChatClient` implements `IChatClient` and answers each request from the recorded exchange matching its digest — in recorded order, digest-checked; mismatch and exhaustion produce actionable errors. Replay replaces recorder + provider in the factory pipeline, so tests still run through telemetry and logging.
- A committed sample fixture (recorded from a scripted provider — no API keys) and a round-trip test: record → replay → identical streams, through the full factory pipeline.
- Out of scope: real-provider fixtures (recorded by the maintainer, used from T-06), `StreamAccumulator` (T-06), session files (Layer 2).

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `ai-layer`: adds the recording/replay contract (fixture format, `LUNATE_RECORD` wiring, replay determinism, no-key replay).

## Impact

- `src/Lunate.Ai` (`RecordingChatClient`, `ReplayChatClient`, factory wiring; `PublicAPI.Unshipped.txt`), `tests/Lunate.Ai.Tests`, `tests/fixtures/streams/` (new committed fixture), `.gitignore` if needed. No new packages.

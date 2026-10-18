# Runtime replay seam

## Why

`ReplayChatClient` replays recorded provider streams deterministically and keyless — but it is reachable only through the tests' constructor seam. The runtime factory has an environment seam for recording (`LUNATE_RECORD`) and none for replay, so a real binary cannot be driven from a fixture. The user-docs media (card B) needs exactly that: a demo recorded once, replayed forever with no key, no network and no run-to-run variance.

## What changes

- `ChatClientFactory` gains `LUNATE_REPLAY=<fixture path>`: when set, the pipeline serves the fixture through `ReplayChatClient` — no credential resolution, no provider client construction, no network.
- Precedence: an explicit recording request wins when both are set (deterministic, documented; no new error path).
- The explicit-credential overload (`Create(model, apiKey)`, used by extension-declared providers) is unchanged.

## Impact

- `src/Lunate.Ai/ChatClientFactory.cs` (the seam), tests for the factory and one session-level keyless replay.
- `openspec/specs/ai-layer/spec.md` (the recording/replay requirement gains the factory seam + two scenarios).
- `docs/architecture.md`: one line next to the recording note.
- No new packages. Enables card B's media (and future demos) without a live key.

## Why

T-06 completes Phase 1's provider story. The factory currently has one concrete adapter (`openai`, the OpenAI-compatible path). This change adds the second — Anthropic, via the official Anthropic .NET SDK wrapped in our own `IChatClient` — and the `StreamAccumulator` that guarantees the loop only ever sees complete function calls, whatever an adapter streams. It also closes the local-endpoint ergonomics gap from T-04 (an API key is currently required even when a custom endpoint is set) and prepares the maintainer fixture-recording step that makes every later card replayable.

## What Changes

- **Anthropic provider**: `AnthropicChatClient : IChatClient` in `Lunate.Ai`, built on the official `Anthropic` package (12.x — the official Claude SDK for C# since v10; verified on nuget.org 2026-10-05). Maps MEAI messages/tools/streaming to the Messages API and back; auth via `ANTHROPIC_API_KEY` (auth.json/settings arrive with T-16); adapter name `anthropic` (the OpenAI-compatible adapter stays `openai`, per the maintainer's naming decision).
- **`StreamAccumulator`**: one `DelegatingChatClient` in the pipeline (OpenTelemetry → logging → accumulator → recorder → provider) that assembles streamed function-call argument fragments into complete calls; recordings stay raw provider output, and the accumulator is tested against recorded streams from every provider.
- **Catalog**: built-in `models.json` gains Anthropic starter entries.
- **Local-endpoint polish**: when a custom endpoint is set and no API key is configured, the OpenAI-compatible path no longer requires a dummy key.
- **Contract tests**: live-gated (`LUNATE_LIVE=1`) tests for both providers; the maintainer records the committed fixtures once (needs keys), then everything replays.
- **ADR-0010** (proposed) + the guide's tech-stack row and pipeline-order lines updated.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `ai-layer`: adds the Anthropic adapter contract, the "complete function calls at the consumer" guarantee, and the local-endpoint key relaxation.

## Impact

- `src/Lunate.Ai` (new adapter + accumulator + factory wiring; `PublicAPI.Unshipped.txt`), `tests/Lunate.Ai.Tests`, `tests/fixtures/streams/` (provider fixtures after the maintainer recording session), `adr/0010-anthropic-adapter.md`, guide tech-stack row + pipeline-order lines. New package: `Anthropic` (official, 12.x — exact pin in the design).

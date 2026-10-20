# Prompt caching (Pi parity P1)

## Why

Every turn re-sends the full context — system prompt, tool declarations and the whole history — and Anthropic bills all of it as fresh input. A long session therefore pays repeatedly for a prefix that never changes. Anthropic prompt caching makes that prefix a cache read after the first turn; the request only needs cache breakpoints, and the response already reports what was read and written.

## What Changes

- **Anthropic request markers**: the Anthropic path marks three cache breakpoints — the last system block, the last tool declaration, and the last content block of the latest message (rolling). At most four breakpoints exist per request; OpenAI requests carry none (its caching is automatic). The existing thinking parameters compose unchanged.
- **Cache-stable prefix**: the system prompt, tool list and (when unchanged) extension sections lead every request byte-identically across turns, so the cached prefix is reusable. Core prompt composition stays once per harness; the rolling breakpoint moves with the latest message.
- **Cache usage accounting**: cache-read and cache-write tokens leave the provider response through `UsageDetails` (`CachedInputTokenCount` and `AdditionalCounts["CacheCreationInputTokens"]`). No event shape change is needed; unknown counts stay absent.
- **Display**: `/context` shows cache read/write tokens when the provider reported them; `--json` usage output carries the same two counts. Neither invents a zero.
- **Tests**: a provider-wire request-body test (real Anthropic adapter, capturing handler), a two-turn rolling-breakpoint/prefix test, a max-four assertion, a harness prefix-stability test, and an opt-in `LUNATE_LIVE` smoke where turn two reports a cache read.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ai-layer`: Anthropic requests carry request-local cache breakpoints; OpenAI carries none; the prefix stays stable across turns.
- `agent-events`: `UsageUpdated` carries the provider's cache read/write counts when reported, without changing the event's shape.
- `tui`: `/context` shows cache read/write tokens when known.
- `print-mode`: `--json` usage output shows cache read/write tokens when known.

## Impact

- **Code**: `src/Lunate.Ai` (Anthropic-only marker client, factory wiring), `src/Lunate.Agent` (usage extraction helper used by consumers), `src/Lunate.Coding` (`/context`, `PrintEventJson`), `src/Lunate.Tui` (context readout helper if needed).
- **Public API**: none — `UsageUpdated` keeps `UsageDetails`, and the marker client is internal. No new NuGet packages.
- **ADRs**: an addendum to ADR-0010 (the Anthropic adapter seam) records the delegating-client decision; ADR-0017 needs no addendum because the event shape is unchanged.
- **Non-goals**: OpenAI cache markers, a cache-hit metric in the footer, session-format changes, and cache control on tool results or history messages beyond the rolling latest block.

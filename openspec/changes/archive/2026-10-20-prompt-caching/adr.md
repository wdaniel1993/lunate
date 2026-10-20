# 0010 (addendum) — Prompt caching on the Anthropic adapter

- Status: proposed — drafted with the `prompt-caching` change (Pi parity P1)
- Date: 2026-10-20
- Relates to: ADR 0010 (Anthropic adapter), ADR 0003 (the loop owns tool calls), ADR 0017 (extensibility event surface — unchanged)

## Context

ADR 0010 chose the official Anthropic SDK and its first-party Microsoft.Extensions.AI adapter. The adapter converts `ChatMessage` content to wire blocks itself, including signed thinking blocks and tool results, and it appends the `ChatOptions` messages to any `MessageCreateParams` returned by `ChatOptions.RawRepresentationFactory` (verified against Anthropic 12.53.0). The existing thinking path uses that factory only for `Model`, `MaxTokens` and `Thinking`, leaving `Messages` empty.

Prompt caching needs cache-control markers on the request prefix. Building the complete request in the factory would duplicate every message, so the marker seam has to be above the adapter's conversion.

## Decision

1. **Delegating marker client, not a raw request factory.** `Lunate.Ai` adds an internal Anthropic-only `AnthropicPromptCache : DelegatingChatClient`. It clones the request, marks the last system block, the last tool declaration and the last content block of the latest message with `CacheControlEphemeral`, and calls the adapter. It sits below `AnthropicTurnMerge` and above the recorder/provider, so the rolling marker lands on the final merged last block.
2. **Three breakpoints, hard bound four.** System, last tool and rolling latest message are the markers this client adds; it counts every breakpoint present on the outgoing request, including any pre-existing markers, and throws `InvalidOperationException` in all builds when the total exceeds the API's four-breakpoint limit. OpenAI requests carry no markers.
3. **Request-local.** History, prompt sections and registered tool declarations are never mutated; a marker added for one turn disappears with that request. The system/tool prefix stays byte-identical across turns of one harness.
4. **Usage needs no new surface.** The adapter already maps cache reads to `UsageDetails.CachedInputTokenCount` and cache creations to `AdditionalCounts["CacheCreationInputTokens"]`; `InputTokenCount` includes both. `UsageUpdated` keeps its shape, so ADR 0017 and the extension event surface are unchanged.
5. **Display is explicit, never synthetic.** `/context` and `--json` show cache read/write only when the provider reported a positive count; a reported zero is treated as unknown and omitted, never shown as `read 0`. The footer is unchanged and cache counts never re-enter the token budget.

## Consequences

- The cached prefix is reusable from the second turn on; the first turn pays cache creation, later turns pay a cache read.
- A request that already carries cache markers and would exceed four breakpoints fails loudly before it reaches the provider instead of silently dropping a marker or sending an invalid request.
- A no-cache turn reports zero rather than null from the adapter; the display treats that as unknown, so the first turn has no `cache:` segment.
- Recordings and replays are unaffected: the recorder sits below the marker client and the request digest ignores cache-control metadata.
- A session reload or an extension `RunStarting` section edit intentionally starts a new prefix; that is a cache miss by design, never silent mid-turn drift.
- No new packages, no public API change, no session-format change.

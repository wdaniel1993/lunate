# Design — prompt-caching

## Context

- `ReasoningParameters.Apply` already installs a `RawRepresentationFactory` for `budget` models (`src/Lunate.Ai/ReasoningParameters.cs:97`); it sets `Model`, `MaxTokens`, `Thinking` and leaves `Messages` empty so the adapter fills them.
- The Anthropic adapter is the first-party one in `Anthropic` 12.53.0 (`Microsoft.Extensions.AI.AnthropicClientExtensions`). Adapter source and a local probe confirm it **appends** its own messages to any `MessageCreateParams.Messages` the factory returns: `Messages = [.. createParamsOriginalMessages ?? [], .. messages]`. A factory that builds messages would duplicate the conversation.
- The same adapter natively supports cache control: `AIContentCacheExtensions.WithCacheControl` marks text/reasoning/image/tool blocks, and `AITool.AdditionalProperties[nameof(Tool.CacheControl)]` marks a tool declaration. The adapter maps both to `cache_control` on the wire.
- `UsageDetails` from the adapter already carries cache reads (`CachedInputTokenCount`) and cache creations (`AdditionalCounts["CacheCreationInputTokens"]`, set only when > 0). `InputTokenCount` is the sum of cache read + cache creation + uncached input.
- `AgentHarness.BuildRequest` joins all prompt sections into one system message; `BuildOptions` attaches the registered tools and applies `ReasoningParameters`. `AnthropicTurnMerge` merges consecutive wire-user messages immediately above the provider.
- `/context` uses `StatusFooterRenderer.PlainText` plus its own segments (`InteractiveSession.Commands.cs:143`); `--json` serializes `UsageDetails` (`PrintEventJson.cs:153`).

## Decisions

### D1 — Breakpoint placement and seam

**Intent (unchanged):** one Anthropic path places cache breakpoints on the last system block, the last tool declaration and the last content block of the latest message (rolling). At most four breakpoints per request; assert the bound. OpenAI gets no markers because its caching is automatic.

**Verified seam correction:** the brief's assumption that the raw-request factory can build the complete `MessageCreateParams` (including messages) does not hold for Anthropic 12.53.0 — the adapter appends the `ChatOptions` messages to whatever the factory returned. Building messages in the factory would send every turn twice. The factory therefore stays what it is: the thinking/max-token seam with `Messages = []`.

The cache seam is an Anthropic-only `AnthropicPromptCache : DelegatingChatClient` in `Lunate.Ai`:

- It clones the request messages, marks the last system `TextContent` and the last content block of the last message with `WithCacheControl(new CacheControlEphemeral())`, and wraps the last tool so `AdditionalProperties["CacheControl"]` carries the same marker. History, prompt sections and registered tool declarations are never mutated.
- It sits **below** `AnthropicTurnMerge` and **above** the recorder/provider, so the rolling marker lands on the final merged last block (a steering message appended to a tool result must not leave the marker in the middle).
- It counts every breakpoint present on the outgoing request — its own three plus any pre-existing `cache_control` markers on content blocks or tool declarations — and throws `InvalidOperationException` in all builds before calling the inner client when the total exceeds four.
- `ChatClientFactory` inserts it only for `provider == "anthropic"`. The OpenAI path is untouched.

Files: `src/Lunate.Ai/AnthropicPromptCache.cs` (new), `src/Lunate.Ai/ChatClientFactory.cs`, `tests/Lunate.Ai.Tests/AnthropicPromptCacheTests.cs`.

Wire assertion: request bodies through the real adapter with a capturing handler (the `AnthropicThinkingBudgetTests` pattern), not a mocked adapter. Assert the JSON has `cache_control` exactly on the last system block, the last tool and the latest last block; run two turns and assert the first turn's rolling marker is gone from turn two while system/tool bytes are identical.

### D2 — Prefix stability

Core prompt composition is once per harness build: `InteractiveSession.BuildHarness` and `PrintMode` call `SystemPrompt.Compose` when the harness is constructed, and the resulting string lives in `AgentHarnessOptions.SystemPrompt`. Within one harness the system message, tool list and skill section are byte-identical across turns. A `/model`, `/new` or resume rebuild recomposes; that is a deliberate new prefix, not mid-turn drift.

The date is computed inside `SystemPrompt.Compose` (`SystemPrompt.cs:33`), not stored in a session field. Consecutive turns of one harness therefore keep the build-time date, which is the property the cache needs; a rebuild on a later day changes it. The test pins the property that matters: two consecutive runs of one harness send identical system bytes.

**Recon correction:** extension prompt sections are not strictly "load once". The `RunStarting` hook runs at the start of every run and may return `SectionEdits` that rewrite or add sections (`AgentHarness.cs:291-307`). Core sections stay stable, but an extension can intentionally invalidate the prefix between turns. The spec scopes the guarantee to core sections and unchanged sections; a cache-invalidating edit is the extension's choice. The prefix-stability test runs without extensions.

Files: no source change expected; `tests/Lunate.Agent.Tests/AgentPrefixStabilityTests.cs` records both requests and compares the system message and tool declarations.

### D3 — Cache usage out of the provider

The adapter already does the extraction. Decision: **no change to `UsageUpdated` and no new usage snapshot record.** The event keeps `UsageDetails`; extensions keep compiling; `PublicAPI.Unshipped.txt` is unchanged. Consumers read:

- cache read: `UsageDetails.CachedInputTokenCount` (nullable);
- cache write: `UsageDetails.AdditionalCounts?["CacheCreationInputTokens"]` (nullable, absent when zero).

A reported zero means the adapter saw no cache access on that turn, not a known zero count: the display layer treats `0` as unknown and omits it, so a first turn never prints `cache: read 0`.

Budget accounting is unchanged: `InputTokenCount` already includes cached and cache-creation tokens, so `AccumulateUsage` must not add cache counts again. The display layer treats the counts as read-only metadata.

Files: no `Lunate.Agent` surface change. `InteractiveSession.Events.AddUsage` and `PrintEventJson.UsageBody` read the two fields; a private helper or constant in each keeps the key spelling in one place. No ADR-0017 addendum is required because the extension event shape does not change; the seam decision is recorded as an ADR-0010 addendum (`adr.md`).

### D4 — Display

- `/context`: accumulate `_cacheReadTokens` and `_cacheWriteTokens` from each `UsageUpdated` alongside the existing input/output totals. Append `cache: read <n>, write <m>` using `StatusFooterRenderer.FormatTokens` (invariant). Emit only the counts that are known: `cache: read <n>`, `cache: write <m>`, both, or no segment at all; a provider-reported `0` counts as unknown and is omitted like a null. The footer is unchanged.
- `--json`: `PrintEventJson.UsageBody` adds `cacheReadTokens` and/or `cacheWriteTokens` next to the existing `usage` object, only when the provider sent a positive count; a reported zero is omitted like an absent count. The `usage` object keeps its shape; raw `cachedInputTokenCount` / `additionalCounts` remain as the adapter produced them.

Files: `src/Lunate.Coding/InteractiveSession.Events.cs` (accumulate), `src/Lunate.Coding/InteractiveSession.Commands.cs` (`ContextText`), `src/Lunate.Coding/PrintEventJson.cs`, matching tests in `tests/Lunate.Coding.Tests`.

### D5 — Tests

1. **Wire body** (`tests/Lunate.Ai.Tests/AnthropicPromptCacheTests.cs`): real `AnthropicClient` + capturing `HttpMessageHandler`; assert `cache_control` on the last system block, last tool, latest last block; count ≤ 4. A request whose history already carries markers plus the three added ones fails loudly with `InvalidOperationException`; exactly four is sent.
2. **Rolling + prefix** (same file): two sequential requests through the factory; assert identical system/tool bytes, turn one's latest-message marker not repeated, turn two marker on its own last block. The latest block is also exercised as a tool result and as a tool call.
3. **Max four** (same file): several system blocks/tools; assert the marker count stays ≤ 4 (the client adds at most three).
4. **Prefix stability** (`tests/Lunate.Agent.Tests/AgentPrefixStabilityTests.cs`): scripted client records two runs; assert byte-identical system message and tool declarations.
5. **Display** (`tests/Lunate.Coding.Tests/InteractiveSessionCommandTests.cs`, `PrintEventJsonTests.cs`): `/context` segments with read-only, write-only, both and neither; JSON fields present/absent and default stdout unchanged. A provider-reported zero is absent from both displays while the raw `usage` object keeps it.
6. **Live smoke** (`tests/Lunate.Ai.Tests/LiveContractTests.cs`): `LUNATE_LIVE=1` + `ANTHROPIC_API_KEY`; a two-turn request with a system prompt large enough to cache; assert turn two's usage has `CachedInputTokenCount > 0`; skipped without the flag.

## Recon contradictions (recorded, not silently resolved)

1. **Factory messages are appended, not conditionally filled** — D1's outcome stands, the seam is corrected to the delegating marker client above.
2. **The prompt date is recomposed per `Compose` call**, not pinned in a session field; it is stable only within one harness build. D2's test targets that guarantee.
3. **Extension prompt sections can change per run** through `RunStarting` edits, so "load once" is true for the skills section and core prompt, not for arbitrary extensions. The spec states the extension-owned exception.
4. **No usage event change is needed**: the adapter already reports both counts through `UsageDetails`, so the smallest surface is zero PublicAPI change.

## Non-goals

OpenAI cache markers, a cache-hit ratio in the footer, caching tool results or historical messages beyond the rolling latest block, session-format changes, and new packages.

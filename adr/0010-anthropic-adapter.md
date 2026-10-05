# 0010 — Anthropic adapter: official SDK and its first-party MEAI adapter

- Status: proposed — awaiting maintainer sign-off
- Date: 2026-10-05 (amended 2026-10-05: maintainer chose option A after the SDK finding)
- Relates to: ADR 0002 (layering, MEAI types), ADR 0003 (own loop), guide tech stack ("Anthropic via an `IChatClient` implementation (check the official SDK first)")

## Context

T-06 adds the second provider adapter. The guide says to check the official SDK first. Verified on nuget.org (2026-10-05): the `Anthropic` package (12.x) is the official Claude SDK for C# (as of v10+), maintained by Anthropic. During apply it surfaced that the SDK **ships a first-party Microsoft.Extensions.AI adapter**: `AsIChatClient(...)` returns an `AnthropicChatClient : IChatClient` that maps messages, tools, streaming, finish reasons, prompt caching and thinking modes. Alternatives: community `Anthropic.SDK` (Grant Hamm), `tryAGI.Anthropic` (previously occupied the `Anthropic` id), or writing our own mapping (option B).

## Decision

- Use the official `Anthropic` package (pinned exactly), **including its first-party MEAI adapter** (`AsIChatClient`). No custom mapping, no third-party MEAI adapter package (option A, maintainer choice 2026-10-05).
- The factory constructs `AnthropicClient` with `ANTHROPIC_API_KEY`, an optional base URL, and **`MaxRetries = 0`** so retry behavior is exclusively the loop's (T-10); it passes the model id and uses the adapter's defaults for max output tokens and thinking mode until a need appears. The OpenAI-compatible path disables transport retries the same way (`OpenAIClientOptions.RetryPolicy = new ClientRetryPolicy(maxRetries: 0)`).
- Adapter names: `openai` (OpenAI-compatible protocol path) and `anthropic` stay as named by the maintainer.

## Consequences

- **No telemetry conflict** (verified in SDK source): the package contains no `ActivitySource` and no `ILogger` usage — our pipeline's OpenTelemetry and logging middleware remain the only instrumentation; nothing nests or duplicates.
- **Errors**: the SDK's typed hierarchy (`Anthropic4xxException`, `Anthropic5xxException`, `AnthropicIOException`, `AnthropicBadRequestException`, …) gives `ProviderErrors` (T-10) a clean retryable/non-retryable taxonomy.
- **Tool calls**: the SDK assembles streamed argument fragments internally (`input_json_delta` into per-block buffers), so the `StreamAccumulator` is a no-op for Anthropic — it remains the pipeline guarantee for other adapters.
- **Drawbacks accepted**: mapping correctness and its tests are the SDK's responsibility (no scripted-event unit-test seam; covered by live-gated contract tests and recorded fixtures); mapping fixes arrive via SDK releases (pinned exact, and the factory keeps option B swappable); the SDK's defaults become our defaults (tunable via its parameters).
- Alternatives rejected: community `Anthropic.SDK` (not official); `tryAGI.Anthropic` (generated, larger surface); custom mapping (option B — full control at the cost of ~300 lines we would own and maintain; revisit only if a mapping need appears).

## Context

Guide Layer 1: "Anthropic via an `IChatClient` implementation (check the official SDK first)" and "The loop acts only on complete function calls. If an adapter streams arguments in pieces, one `StreamAccumulator` assembles them, tested on recorded streams from every provider." T-04 built the factory, catalog and the OpenAI-compatible path; T-05 built recording/replay. This change adds the second adapter and the accumulator.

**Amendment (2026-10-05, maintainer decision — option A):** the official `Anthropic` package (12.53.0) ships a first-party MEAI adapter (`Microsoft.Extensions.AI.AnthropicClientExtensions.AsIChatClient`, a private `AnthropicChatClient : IChatClient`). The maintainer chose to use it instead of writing our own mapping (~300 lines we would own and maintain). Verified in the SDK source before deciding: no `ActivitySource` and no `ILogger` usage in the package (no telemetry/logging duplication with our middleware); typed exception hierarchy (`Anthropic4xxException`, `Anthropic5xxException`, `AnthropicIOException`, `AnthropicBadRequestException`, …) for `ProviderErrors`; tool-call arguments are assembled internally (`input_json_delta` accumulation into per-block buffers).

## Goals / Non-Goals

**Goals:** Anthropic as a first-class provider behind the factory; a pipeline guarantee that consumers only see complete function calls; raw recordings; local endpoints usable without dummy keys; contract tests + fixtures prepared for the maintainer's recording session.

**Non-Goals:** auth.json/settings loading (T-16); retries (loop, T-10); further providers; ACP/TUI surface work; custom Anthropic protocol mapping (option B — revisited only if a mapping need appears; the factory keeps the choice swappable).

## Decisions

- **Anthropic adapter (option A)**: official `Anthropic` package (12.x, exact pin at apply from nuget.org) via its **first-party MEAI adapter**: `AnthropicClient` (auth `ANTHROPIC_API_KEY`, optional `ClientOptions.BaseUrl`, `MaxRetries = 0`) → `.AsIChatClient(modelId)`. `defaultMaxOutputTokens` and `thinkingMode` use the adapter's defaults until a need appears. The factory is the only construction point. See ADR-0010.
- **Testability**: mapping correctness is the SDK's responsibility. Ours: factory-wiring tests (construction without network, provider selection, `MaxRetries = 0`), live-gated contract tests (`LUNATE_LIVE=1`), and recorded fixtures. The injectable-seam task from the pre-amendment design is dropped.
- **SDK-level retries**: resolved for both providers — Anthropic `MaxRetries = 0` and OpenAI `RetryPolicy = new ClientRetryPolicy(maxRetries: 0)` — so retry behavior stays exclusively the loop's (T-10). Recorded in ADR-0010.
- **StreamAccumulator**: internal `DelegatingChatClient`; merges `FunctionCallContent` fragments by `CallId` (concatenating argument JSON fragments, keeping the name from the first fragment); passes everything else through unchanged; a no-op when calls are already complete (which covers Anthropic — verified). Fragment representation: a `FunctionCallContent` whose `Arguments` contains exactly one entry under the reserved `$arguments` key holding a raw JSON fragment (a string, or a `JsonElement` of string kind); fragments merge by call id and are emitted as one parsed call in a synthesized update at stream end. Placement: **between logging and recorder** (outermost-first: OpenTelemetry → logging → accumulator → recorder → provider), so the recorder captures raw provider output and the replay path still exercises the accumulator. Guide pipeline-order lines updated to match.
- **Catalog**: add a small Anthropic starter set (`claude-sonnet-5-5`, `claude-opus-5-5`; 1M-token context window, verified on platform.claude.com 2026-10-05) to the embedded `models.json`; user overrides unchanged.
- **Local-endpoint polish**: when `ModelInfo.Endpoint` is set and no key is configured, the OpenAI-compatible path constructs the client with a placeholder credential instead of failing (local servers ignore it).
- **Contract tests**: `LUNATE_LIVE=1` gated; skipped otherwise; both providers; document required env vars in the test file.
- **Fixtures**: the maintainer records one fixture per provider via `LUNATE_RECORD=1` + `LUNATE_RECORD_PATH` (needs keys); committed under `tests/fixtures/streams/`; the accumulator and later loop tests consume them.
- **Packages**: only `Anthropic` (official). Guide rule ("adding a package is a design decision") satisfied by ADR-0010 + the tech-stack row update in this change.

## Risks / Trade-offs

- [SDK churn] → exact pin; adapter isolated behind the factory; contract tests catch surface changes.
- [Mapping gaps] → mapping is the SDK's, but contract tests + recorded fixtures verify the integration we ship; the factory keeps option B one swap away.
- [Accumulator over/under-merging] → fragment-merge tests incl. multiple concurrent calls, interleaved text, and already-complete pass-through.
- [Reserved fragment key collision] → a complete call whose only real argument is named `$arguments` would be misread as a fragment. The key is reserved and documented here and in the spec; only exactly one-entry dictionaries whose value is a string (or string-kind `JsonElement`) count as fragments.
- [Fragment ordering] → text after a call keeps its position, but assembled calls land in one synthesized update at stream end, so a call may be reordered relative to later text. Consumers key on call id and the loop acts only on complete calls, so this is acceptable.
- [SDK/abstractions version skew] → the Anthropic SDK targets Microsoft.Extensions.AI.Abstractions 10.5.1 while we pin 10.10.1; NuGet unifies upward and the live-gated contract tests plus recorded fixtures are the guard.

## Migration Plan

Not applicable — additive.

## Open Questions

- None open — the adapter decision (option A) and the SDK retry setting are resolved and recorded in ADR-0010.

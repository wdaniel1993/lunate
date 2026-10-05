## Context

Guide Layer 1: "Anthropic via an `IChatClient` implementation (check the official SDK first)" and "The loop acts only on complete function calls. If an adapter streams arguments in pieces, one `StreamAccumulator` assembles them, tested on recorded streams from every provider." T-04 built the factory, catalog and the OpenAI-compatible path; T-05 built recording/replay. This change adds the second adapter and the accumulator.

## Goals / Non-Goals

**Goals:** Anthropic as a first-class provider behind the factory; a pipeline guarantee that consumers only see complete function calls; raw recordings; local endpoints usable without dummy keys; contract tests + fixtures prepared for the maintainer's recording session.

**Non-Goals:** auth.json/settings loading (T-16); retries (loop, T-10); further providers; ACP/TUI surface work.

## Decisions

- **Anthropic adapter**: official `Anthropic` package (12.x, exact pin at apply from nuget.org) behind our own `IChatClient` implementation in `Lunate.Ai` (internal; the factory is the only construction point). No third-party MEAI adapter — see ADR-0010. Auth: `ANTHROPIC_API_KEY`; optional base-URL override if the SDK supports one (for proxies); inspect the pinned SDK surface during apply.
- **Testability without network**: the adapter takes an injectable seam for the SDK's stream (a delegate/interface over `MessageCreateParams → IAsyncEnumerable<raw stream events>`), so mapping is unit-tested with scripted SDK event sequences. If the SDK makes a seam impractical, stop and propose an alternative rather than testing only live.
- **SDK-level retries**: decide during apply whether to disable the SDK's transport retries so retry behavior stays exclusively the loop's (T-10); record the outcome in ADR-0010 consequences.
- **StreamAccumulator**: internal `DelegatingChatClient`; merges `FunctionCallContent` fragments by `CallId` (concatenating argument JSON fragments, keeping the name from the first fragment); passes everything else through unchanged; a no-op when calls are already complete. Placement: **between logging and recorder** (outermost-first: OpenTelemetry → logging → accumulator → recorder → provider), so the recorder captures raw provider output and the replay path still exercises the accumulator. Guide pipeline-order lines updated to match.
- **Catalog**: add a small Anthropic starter set (for example `claude-sonnet-4.6`, `claude-opus-4.7`) to the embedded `models.json`; user overrides unchanged.
- **Local-endpoint polish**: when `ModelInfo.Endpoint` is set and no key is configured, the OpenAI-compatible path constructs the client with a placeholder credential instead of failing (local servers ignore it).
- **Contract tests**: `LUNATE_LIVE=1` gated; skipped otherwise; both providers; document required env vars in the test file.
- **Fixtures**: the maintainer records one fixture per provider via `LUNATE_RECORD=1` + `LUNATE_RECORD_PATH` (needs keys); committed under `tests/fixtures/streams/`; the accumulator and later loop tests consume them.
- **Packages**: only `Anthropic` (official). Guide rule ("adding a package is a design decision") satisfied by ADR-0010 + the tech-stack row update in this change.

## Risks / Trade-offs

- [SDK churn] → exact pin; adapter isolates it; contract tests catch surface changes.
- [Mapping gaps (tools, stop reasons, usage)] → scripted-event unit tests + recorded fixtures.
- [Accumulator over/under-merging] → fragment-merge tests incl. multiple concurrent calls and interleaved text.

## Migration Plan

Not applicable — additive.

## Open Questions

- SDK retry default (resolved during apply, recorded in ADR-0010).

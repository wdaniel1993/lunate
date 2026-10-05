## 1. Anthropic adapter (option A)

- [x] 1.1 Pin the official Anthropic package; factory constructs `AnthropicClient` (`ANTHROPIC_API_KEY`, optional base URL, `MaxRetries = 0`) and wraps it via `AsIChatClient(modelId)`
- [x] 1.2 Factory-wiring tests (provider selection, construction without network, retry setting)
- [x] 1.3 Catalog starter entries for Anthropic

## 2. StreamAccumulator

- [x] 2.1 Accumulator middleware (merge by call id, fragment concatenation, already-complete pass-through) + unit tests (multiple concurrent calls, interleaved text)
- [x] 2.2 Pipeline placement (accumulator between logging and recorder) + pipeline-order test update; guide lines updated

## 3. Local-endpoint polish

- [x] 3.1 Placeholder credential when a custom endpoint is set and no key is configured + tests

## 4. Contract + fixtures

- [x] 4.1 `LUNATE_LIVE=1` contract test scaffold for both providers (env vars documented; skipped otherwise)
- [ ] 4.2 Maintainer recording session: one fixture per provider recorded and committed under `tests/fixtures/streams/` (needs keys; maintainer)

## 5. Docs

- [x] 5.1 ADR-0010 (proposed, amended for option A); guide tech-stack row updated to name the official package + first-party adapter

## 6. Close

- [x] 6.1 `scripts/verify.sh` green; reviewer pass; fix what it reports
- [x] 6.2 `openspec validate add-anthropic-stream-accumulator --type change --strict`; commit per group

## 7. Adversarial review fixes (2026-10-05)

- [x] 7.1 Provider runtime assets (Anthropic, OpenAI, Microsoft.Extensions.AI) reach the app build output; `verify.sh` checks for them
- [x] 7.2 Catalog ids corrected to `claude-sonnet-5-5` / `claude-opus-5-5` with the 1M-token context window (platform.claude.com, 2026-10-05)
- [x] 7.3 Fragment representation (`$arguments` key, merge by call id, synthesized call at stream end) documented in spec and design, with collision and ordering risks
- [x] 7.4 Pipeline layer order pinned by test (OpenTelemetry -> logging -> accumulator -> recorder -> provider); guide reuse list updated
- [x] 7.5 OpenAI transport retries disabled; FinishReason and ShallowCopy regression tests added; SDK/abstractions version-skew risk noted; ADR-0010 decision names both providers
- [x] 7.6 `scripts/verify.sh` green after the fixes

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

- [ ] 4.1 `LUNATE_LIVE=1` contract test scaffold for both providers (env vars documented; skipped otherwise)
- [ ] 4.2 Maintainer recording session: one fixture per provider recorded and committed under `tests/fixtures/streams/` (needs keys; maintainer)

## 5. Docs

- [ ] 5.1 ADR-0010 (proposed, amended for option A); guide tech-stack row updated to name the official package + first-party adapter

## 6. Close

- [ ] 6.1 `scripts/verify.sh` green; reviewer pass; fix what it reports
- [ ] 6.2 `openspec validate add-anthropic-stream-accumulator --type change --strict`; commit per group

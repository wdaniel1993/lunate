## 1. Anthropic adapter

- [ ] 1.1 Pin the official Anthropic package; adapter skeleton with the injectable stream seam; `ANTHROPIC_API_KEY` auth
- [ ] 1.2 Message/tool/stream mapping + unit tests (scripted SDK events, no network)
- [ ] 1.3 Factory wiring (provider `anthropic`) + catalog starter entries

## 2. StreamAccumulator

- [ ] 2.1 Accumulator middleware (merge by call id, fragment concatenation, no-op when complete) + unit tests (multiple concurrent calls, interleaved text)
- [ ] 2.2 Pipeline placement (accumulator between logging and recorder) + pipeline-order test update; guide lines updated

## 3. Local-endpoint polish

- [ ] 3.1 Placeholder credential when a custom endpoint is set and no key is configured + tests

## 4. Contract + fixtures

- [ ] 4.1 `LUNATE_LIVE=1` contract test scaffold for both providers (env vars documented; skipped otherwise)
- [ ] 4.2 Maintainer recording session: one fixture per provider recorded and committed under `tests/fixtures/streams/` (needs keys; maintainer)

## 5. Docs

- [ ] 5.1 ADR-0010 (proposed); guide tech-stack row updated to name the official package

## 6. Close

- [ ] 6.1 `scripts/verify.sh` green; reviewer pass; fix what it reports
- [ ] 6.2 `openspec validate add-anthropic-stream-accumulator --type change --strict`; commit per group

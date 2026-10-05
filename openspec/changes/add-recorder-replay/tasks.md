## 1. Fixture format

- [x] 1.1 Fixture schema + serialization helper (header, exchanges, request digest) with `AIJsonUtilities` options; round-trip guard test

## 2. Recording

- [x] 2.1 `RecordingChatClient` (tee, append-per-exchange, dirs, header-on-create, lock) + tests

## 3. Replay

- [ ] 3.1 `ReplayChatClient` (streaming + aggregated `GetResponseAsync`, digest check, actionable errors) + tests

## 4. Factory wiring

- [ ] 4.1 `LUNATE_RECORD`/`LUNATE_RECORD_PATH` wiring (explicit decorator wins); default path gitignored + tests

## 5. End to end

- [ ] 5.1 Committed sample fixture + record→replay identical test through the factory pipeline (telemetry + logging active)

## 6. Close

- [ ] 6.1 `scripts/verify.sh` green; reviewer pass; fix what it reports
- [ ] 6.2 `openspec validate add-recorder-replay --type change --strict`; commit per group

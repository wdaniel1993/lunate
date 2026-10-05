## 1. Event types

- [x] 1.1 `AgentEvent` base + all concrete sealed records (fields per design) + `ExtensionEvent` base; `PublicAPI.Unshipped.txt` updated
- [x] 1.2 Tests: every concrete type is sealed; extensions are distinguishable; fields construct correctly

## 2. Emission

- [x] 2.1 `IAgentEvents` + internal channel implementation (ordered, completes) + tests
- [x] 2.2 Recording implementation in the tests project

## 3. Sequence rules

- [x] 3.1 Internal `EventSequenceValidator` + valid/invalid sequence tests (missing start, unbracketed text, wrong tool order, event after terminal)

## 4. Close

- [x] 4.1 `scripts/verify.sh` green; reviewer pass; fix what it reports
- [x] 4.2 `openspec validate add-agent-events --type change --strict`; commit per group

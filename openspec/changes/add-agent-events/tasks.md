## 1. Event types

- [ ] 1.1 `AgentEvent` base + all concrete sealed records (fields per design) + `ExtensionEvent` base; `PublicAPI.Unshipped.txt` updated
- [ ] 1.2 Tests: every concrete type is sealed; extensions are distinguishable; fields construct correctly

## 2. Emission

- [ ] 2.1 `IAgentEvents` + internal channel implementation (ordered, completes) + tests
- [ ] 2.2 Recording implementation in the tests project

## 3. Sequence rules

- [ ] 3.1 Internal `EventSequenceValidator` + valid/invalid sequence tests (missing start, unbracketed text, wrong tool order, event after terminal)

## 4. Close

- [ ] 4.1 `scripts/verify.sh` green; reviewer pass; fix what it reports
- [ ] 4.2 `openspec validate add-agent-events --type change --strict`; commit per group

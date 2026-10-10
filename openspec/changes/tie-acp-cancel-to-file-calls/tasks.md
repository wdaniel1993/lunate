# Tasks: Tie the ACP cancel to file round trips

## 1. Run state extraction (Protocols)

- [ ] 1.1 `SessionRunState` (internal) with the moved gate/active/prompts/cancelPending logic + `Token`
- [ ] 1.2 `LibAcpServer`: create the state before the context; `Session` delegates to it; `ClientTextFileAccess` receives it

## 2. Bridge (Protocols)

- [ ] 2.1 `ClientTextFileAccess`: pass `state.Token` into the LibAcp read/write calls
- [ ] 2.2 `Await`: fault-observation continuation on the timeout path

## 3. Tests

- [ ] 3.1 Cancel during a file round trip stops promptly (bounded wall time, response cancelled)
- [ ] 3.2 Abandoned request's late failure is observed (no unobserved exception)
- [ ] 3.3 Existing cancel/latch/timeout/fs suites green unchanged

## 4. Gate

- [ ] 4.1 `bash scripts/verify.sh` green; deviations recorded in design.md

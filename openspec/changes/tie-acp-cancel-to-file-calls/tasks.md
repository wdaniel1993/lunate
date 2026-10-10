# Tasks: Tie the ACP cancel to file round trips

## 1. Run state extraction (Protocols)

- [x] 1.1 `SessionRunState` (internal) with the moved gate/active/prompts/cancelPending logic + `Token`
- [x] 1.2 `LibAcpServer`: create the state before the context; `Session` delegates to it; `ClientTextFileAccess` receives it

## 2. Bridge (Protocols)

- [x] 2.1 `ClientTextFileAccess`: pass `state.Token` into the LibAcp read/write calls
- [x] 2.2 `Await`: fault-observation continuation on the timeout path

## 3. Tests

- [x] 3.1 Cancel during a file round trip stops promptly (bounded wall time, response cancelled)
- [x] 3.2 Abandoned request's late failure is observed (no unobserved exception)
- [x] 3.3 Existing cancel/latch/timeout/fs suites green unchanged

## 4. Gate

- [ ] 4.1 `bash scripts/verify.sh` green; deviations recorded in design.md

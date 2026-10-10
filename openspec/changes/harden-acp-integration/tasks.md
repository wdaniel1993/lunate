# Tasks: Harden the ACP integration (T-28 follow-ups)

## 1. Probe (Coding)

- [ ] 1.1 `ITextFileAccess.ReadPrefix` + `LocalTextFileAccess` implementation (PublicAPI entry)
- [ ] 1.2 `ReadTool` probe-first flow (exact size on the probe path; whole-text check only when the provider cannot probe)

## 2. Bounded round trips + taxonomy (Protocols)

- [ ] 2.1 `ClientTextFileAccess`: `ReadPrefix` → null; `WaitAsync` timeout → `IOException`
- [ ] 2.2 `ClientTextFileAccess`: not-found mapping; `Exists` rethrows non-not-found
- [ ] 2.3 `ClientApprover`: permission timeout → decline + log

## 3. Docs

- [ ] 3.1 `docs/guide.md` sketch refresh (`AcpSessionContext` factory)

## 4. Tests

- [ ] 4.1 Local probe cases (exact sizes, window boundary, empty)
- [ ] 4.2 Client-backed: binary whole-text path, not-found vs other errors, `Exists` rethrow
- [ ] 4.3 Timeouts: file request error, permission decline (short injected values)

## 5. Gate

- [ ] 5.1 `bash scripts/verify.sh` green; deviations recorded in design.md

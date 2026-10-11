# Tasks: slim the windows-latest verify job

## 1. The fix

- [x] 1.1 Root cause via probe branch (`probe/windows-test-exit`): `tee` + MSBuild node-reuse workers/MSBuild server holding the inherited pipe; snapshots during the stall; no stall without a pipe
- [x] 1.2 `scripts/verify.sh`: `MSBUILDDISABLENODEREUSE=1` + `DOTNET_CLI_USE_MSBUILD_SERVER=0`
- [x] 1.3 `scripts/verify.ps1`: same (parity)
- [x] 1.4 Probe re-run with the fix: windows `verify.sh` green, 1858 tests, both passes

## 2. The bump (kept, independent)

- [x] 2.1 `global.json` 10.0.103 → 10.0.401; local full gate green under it (no analyzer/formatter drift)

## 3. Spec

- [ ] 3.1 ci-pipeline: MODIFIED "SDK pinning" (band policy) + ADDED "no lingering build workers" (both in the delta)

## 4. Measurement (in the PR)

- [ ] 4.1 Push; extract the windows legs' step timestamps; record before/after in the PR body (stalls gone, totals)
- [ ] 4.2 All checks green with identical test counts; probe branch deleted; deviations recorded

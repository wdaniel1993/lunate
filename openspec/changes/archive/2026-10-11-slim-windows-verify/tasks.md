# Tasks: slim the windows-latest verify job

## 1. The fix

- [x] 1.1 Root cause via probe branch (`probe/windows-test-exit`): captured test steps stalled on pipe EOF; snapshots name the survivors (first MSBuild node-reuse workers + VBCSCompiler; after node-reuse disable, VBCSCompiler alone for 3m16s)
- [x] 1.2 `scripts/verify.sh`: `run_tests` captures to a file and prints at command end (no pipe, no EOF dependency)
- [x] 1.3 `scripts/verify.ps1`: `Invoke-Tests` via `Start-Process` file redirection (`*>` still piped and kept the stall)
- [x] 1.4 Node-reuse / MSBuild-server env vars tried, shaved ~45s, insufficient — reverted (design.md records why)
- [x] 1.5 PR CI with the file capture: all four jobs green, 1858 tests both passes, stalls gone (windows bash 16m45 → 11m17; pwsh test step 5m38 with `*>` → Start-Process in this push)

## 2. The bump (kept, independent)

- [x] 2.1 `global.json` 10.0.103 → 10.0.401; local full gate green under it (no analyzer/formatter drift)

## 3. Spec

- [x] 3.1 ci-pipeline: MODIFIED "SDK pinning" (band policy) + ADDED "Captured gate steps end with their commands" (both in the delta)

## 4. Measurement (in the PR)

- [ ] 4.1 Push; extract the windows legs' step timestamps; record before/after in the PR body (stalls gone, totals)
- [ ] 4.2 All checks green with identical test counts; probe branch deleted; deviations recorded

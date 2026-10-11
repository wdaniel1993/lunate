# Tasks: slim the windows-latest verify job

## 1. The bump

- [ ] 1.1 `global.json` 10.0.103 → 10.0.401
- [ ] 1.2 Install 10.0.401 locally (`~/.dotnet`, side-by-side); full `scripts/verify.sh` green under it (watch for analyzer/formatter drift)

## 2. Spec

- [ ] 2.1 ci-pipeline "SDK pinning" MODIFIED (band policy + scenario; original scenario verbatim)

## 3. Measurement (in the PR)

- [ ] 3.1 Push; extract the windows legs' step timestamps; record before/after in the PR body (gap gone, totals)
- [ ] 3.2 All checks green with identical test counts; deviations recorded

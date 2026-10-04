## 1. CI workflow

- [x] 1.1 Add `.github/workflows/ci.yml`: push/PR triggers, three-OS matrix (ubuntu, macos, windows), `actions/setup-dotnet` 10.0.x, NuGet cache
- [x] 1.2 Per-OS job: run `scripts/verify.sh` with the runner's RID; windows additionally runs `scripts/verify.ps1`
- [x] 1.3 Run `scripts/gate-tests.sh` in the matrix (budget negative path — the "CI turns red" proof)
- [x] 1.4 Upload verification outputs (`artifacts/`) as workflow artifacts

## 2. Release workflow

- [x] 2.1 Add `.github/workflows/release.yml`: `v*` tags plus `workflow_dispatch` dry run
- [x] 2.2 Publish osx-arm64, win-x64 and linux-x64 single-file binaries on their native runners
- [x] 2.3 Attach one archive per target to the GitHub release via `gh` (dry run: workflow artifacts only)

## 3. Prove and close

- [ ] 3.1 Push and confirm the three matrix jobs green on GitHub
- [ ] 3.2 Dispatch the release workflow in dry-run mode; confirm three target artifacts
- [ ] 3.3 Confirm the gate self-tests fail CI when the budget check breaks
- [ ] 3.4 Run `openspec validate add-ci --type change --strict`; commit per group

## Why

The repository's build-and-verification contract exists and one command decides "done" (`scripts/verify.sh`, change `add-repo-skeleton`) — but nothing runs it automatically. Every push is currently trusted; this change wires continuous integration so the contract is enforced on every commit, on all three target OSes, and so releases produce the single-file binaries the guide promises.

## What Changes

- New `.github/workflows/ci.yml`: on every push to `main` and every pull request, a three-OS matrix (ubuntu, macos, windows) runs the full verification — build, tests, single-file publish, startup budget, format, public API — plus the gate self-tests (`scripts/gate-tests.sh`); the windows job additionally runs `scripts/verify.ps1`.
- New `.github/workflows/release.yml`: on `v*` tags (and manually as a dry run), publishes self-contained single-file binaries for `osx-arm64`, `win-x64` and `linux-x64`, and attaches one archive per target to the GitHub release via the `gh` CLI.
- NuGet caching and a pinned `actions/setup-dotnet` 10.0.x in both workflows.
- "CI turns red when a budget is exceeded" — the gate's negative paths (budget breach, gate self-tests) run in CI, completing the review items deferred from `add-repo-skeleton`.

## Capabilities

### New Capabilities
- `ci-pipeline`: continuous integration and release automation — the per-OS verification matrix, budget enforcement in CI, the Windows verification twin, and the three-target release artifacts.

### Modified Capabilities
- None. `repo-foundation`'s behaviours are unchanged; CI enforces them rather than extending them.

## Impact

- New `.github/workflows/`; no product code changes, no new NuGet packages, no marketplace actions beyond the first-party `actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact` and `actions/download-artifact`.
- CI minutes are free for this public repository; per-commit cost is a three-runner matrix.

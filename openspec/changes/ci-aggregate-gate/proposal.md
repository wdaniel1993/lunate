# ci-aggregate-gate: one stable required check for branch protection

## Why

Branch protection can only require status checks by name. If the ruleset required the current job names (`verify (macos-latest)`, `verify (ubuntu-latest)`, `verify (windows-latest)`, `verify-powershell (windows)`), then renaming or restructuring a matrix leg could silently drop coverage from the merge gate — the old required name would simply stop reporting, and a PR could merge unchecked against the new shape.

## What Changes

- **A `ci-ok` aggregate job** in `.github/workflows/ci.yml`: `needs: [verify, verify-powershell]`, `if: always()`, and it fails unless every needed job's result is `success` (a skipped leg is not success). The repository ruleset then requires only `ci-ok`.
- **Confirmed: no path filters on `pull_request`.** The existing `paths-ignore` is push-only (`main` docs-only merges skip the matrix; docs-lint still covers them). PR events are unfiltered, so `ci-ok` reports on every pull request — a required check can never hang pending because a PR touched only filtered paths.
- **Spec delta**: `ci-pipeline` — new "Aggregate merge gate" requirement with scenarios (green, red on failure/skip, always reports).

## Impact

- `.github/workflows/ci.yml` (+~14 lines), `openspec` delta. No code, no packages, no product behavior. The maintainer enables the ruleset (ci-ok only) after this lands on `main`.
- Accepted side effect (maintainer's call, 2026-10-17): macOS becomes a merge gate — a recurrence of the transient runner flake blocks merges until a re-run; the `flake-probe` tripwire workflow is the follow-up if it fires.

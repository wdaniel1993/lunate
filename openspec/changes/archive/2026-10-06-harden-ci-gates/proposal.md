## Why

A full-repo review (2026-10-06) found the verification and release machinery weaker than the specs promise: the public-API check compares the working tree to the index only, so it can never fail in CI; documentation-only pushes to `main` skip linting; the release workflow publishes on any tag without running the gate, without checking that the tag matches the built version, and without checksums; the Windows twin lacks the provider-asset check; the layering test enforces project references but not the `Lunate.Agent` package rule; and a few leftovers (placeholder tests, the settled S-5 probe workflow, untracked Roslyn API surface) remain. This change closes those gaps — scripts, workflows and tests only, no product code.

## What Changes

- **Public-API check fixed**: the Shipped-file check diffs against the merge base with `main` (fallback chain for environments without a remote), so committed branch changes are caught in CI; the logic moves into `lib.sh` and gains a gate self-test in `gate-tests.sh`; the spec scenario is pinned to that comparison base.
- **Docs lint on `main`**: a small `docs-lint` workflow runs on documentation-only pushes (which skip the matrix), so lint violations cannot land unseen.
- **Release hardening**: a release gate job runs the full verification gate and fails when the tag does not match `Directory.Build.props`; the release attaches `SHA256SUMS` next to the archives; all workflows install the SDK pinned in `global.json`.
- **Twin parity**: `verify.ps1` gains the provider-runtime-assets check and the corrected public-API check.
- **Layering enforcement extended**: the architecture test also enforces the `Lunate.Agent` runtime-package rule (build-only analyzer packages exempt).
- **Leftovers**: delete the two real-test-era placeholder test files and the settled `s5-crash-probe` workflow; add PublicAPI tracking to `Lunate.Roslyn` (the last untracked library).

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `ci-pipeline`: docs-only pushes must still lint; release gate, tag/version check and checksums; SDK pinning from `global.json`.
- `repo-foundation`: Shipped-API scenario pinned to the merge-base comparison; layering requirement covers the `Lunate.Agent` package rule.

## Impact

- Scripts: `lib.sh`, `verify.sh`, `verify.ps1`, `gate-tests.sh`. Workflows: `ci.yml`, `codeql.yml`, `release.yml`, new `docs-lint.yml`. Tests: `LayeringChecker`/`LayeringTests` (+ checker unit tests). Projects: `Lunate.Roslyn.csproj` + two `PublicAPI` files. Deletions: two `PlaceholderTests.cs`, `s5-crash-probe.yml`. No product code, no new runtime dependencies; no ADR (gate mechanics — the rules live in the two spec deltas).

## Context

`repo-foundation` defined the gate; the `add-repo-skeleton` review deferred two items to this change: the gate's heavier failure modes (public-API diff, warnings-as-errors) and `verify.ps1` coverage. The guide's T-02 card sets the scope: build and test on three OSes, publish three targets, `scripts/perf.sh`; done when artifacts exist for all targets and CI turns red when a budget is exceeded.

CI flow (job level):

```mermaid
flowchart LR
  push[push / PR] --> matrix{3-OS matrix}
  matrix --> ubuntu[ubuntu: verify.sh + gate-tests]
  matrix --> macos[macos: verify.sh + gate-tests]
  matrix --> windows[windows: verify.sh + verify.ps1 + gate-tests]
  tag[v* tag / dispatch] --> release[release: publish 3 RIDs]
  release --> assets[archives -> GitHub release]
```

## Goals / Non-Goals

**Goals:**
- Every commit held to the full contract on all three target OSes.
- "CI turns red when a budget is exceeded" proven inside CI (gate self-tests).
- Release tags produce the three single-file binaries; a dry-run path proves artifact production without tagging.

**Non-Goals:**
- Branch protection rules, required reviews, scheduled nightly jobs.
- Contract tests against live model providers (no API keys in CI).
- Code signing or notarization of binaries (later distribution change).

## Decisions

- **GitHub Actions, per-OS matrix** (guide decision; public repo → free minutes). Alternative: single ubuntu runner with cross-builds — rejected: ReadyToRun does not cross-compile, and Windows/macOS behaviour must be exercised natively.
- **Each runner publishes its own RID** (`ubuntu→linux-x64`, `macos→osx-arm64`, `windows→win-x64`; `macos-latest` is arm64). Alternative: cross-publish — rejected (R2R).
- **`actions/checkout` + `actions/setup-dotnet` only**; release uploads via the preinstalled `gh` CLI. Alternative: marketplace release actions — rejected (minimal third-party surface).
- **NuGet caching** via `actions/setup-dotnet`'s cache option (or `actions/cache`), keyed on `global.json` and project files.
- **hyperfine installed per runner** (brew on macos; apt or pinned release binary on ubuntu; pinned release binary or choco on windows) — exact method finalized during implementation and pinned.
- **Release workflow has a dry-run dispatch path** so "artifacts for all targets" can be proven without creating a release.
- **Windows runs both gates**: bash gate (Git Bash is present on runners) plus `scripts/verify.ps1` (pwsh) — closing the T-01 coverage gap.

## Risks / Trade-offs

- [Runner SDK or hyperfine availability drifts] → versions pinned; install steps fail loudly naming the tool; revisit when `global.json` moves.
- [macos runner architecture changes] → RID derived from the runner rather than hard-coded where possible.
- [Windows hyperfine install flakiness] → prefer a pinned release-binary download over package managers if needed.
- [Matrix cost/latency] → public-repo minutes are free; keep jobs lean.

## Migration Plan

Not applicable — additive workflows; rollback removes the workflow files.

## Open Questions

- None blocking. Live-provider contract tests remain out of scope (no keys in CI).

# Tasks — roslyn-refresh-rebaseline

## 1. Fix

- [x] 1.1 Reproduction test (failing on the pre-fix tree): `RoslynBackendSyncTests.A_refresh_does_not_reflag_the_file_on_the_next_check` — edit, check (1 changed), check again (none).
- [x] 1.2 Three-phase refresh in `RoslynBackend.cs`: stamp both sides of each read; revalidate before the apply; re-baseline only when the disk still holds the applied text. `DocumentFreshness.FileStamp` + `StampOf` exposed to the backend.
- [x] 1.3 Racing-write coverage (maintainer-requested): `onRefreshRead` test seam; bash-style and bus-style racing edits — not clobbered, re-flagged, converged.
- [x] 1.4 `RoslynBackendSyncTests` + `DocumentFreshnessTests` green (16/16, 2026-10-17).

## 2. Spec and gate

- [ ] 2.1 `agent-tools` delta: refresh does not re-flag + racing writes are never overwritten or swallowed (sentence + scenario).
- [ ] 2.2 `scripts/verify.sh` green; archive `roslyn-refresh-rebaseline`; adversarial review; PR.

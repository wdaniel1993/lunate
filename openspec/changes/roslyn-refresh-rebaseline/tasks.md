# Tasks — roslyn-refresh-rebaseline

## 1. Fix

- [ ] 1.1 Reproduction test (failing on the pre-fix tree): `RoslynBackendSyncTests.A_refresh_does_not_reflag_the_file_on_the_next_check` — edit, check (1 changed), check again (none).
- [ ] 1.2 Re-baseline the refreshed paths' stamps after the refresh apply in `RoslynBackend.cs`.
- [ ] 1.3 The reproduction test passes; `RoslynBackendSyncTests` + `DocumentFreshnessTests` green.

## 2. Spec and gate

- [ ] 2.1 `agent-tools` delta: refresh does not re-flag (sentence + scenario).
- [ ] 2.2 `scripts/verify.sh` green; archive `roslyn-refresh-rebaseline`; adversarial review.

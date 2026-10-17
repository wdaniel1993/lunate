# Design — ci-aggregate-gate

## Decision — a single aggregate job, not four required names

`ci-ok`:
- `needs: [verify, verify-powershell]` — covers the three-OS bash matrix (matrix results aggregate into the `verify` job result) and the PowerShell twin.
- `if: always()` — with a plain (implicit `success()`) condition the job would be *skipped* whenever a leg fails, and a skipped aggregate reports as a skipped check; `always()` plus an explicit result check makes it a definitive red instead.
- Fails unless all needed results are `success` — `skipped` and `cancelled` are not success. (Superseded runs are cancelled by the existing `concurrency` group; those checks are irrelevant because the newest run on the head governs what the ruleset sees.)

## Path filters — audit result

- `pull_request:` has **no** `paths`/`paths-ignore` filters → every PR runs the full matrix and `ci-ok` always reports.
- `push:` keeps `paths-ignore` for docs/spec-only merges (`docs/**`, `adr/**`, `openspec/**`, `*.md`) — intentional and harmless for the gate: required checks evaluate PR heads, and docs-only merges remain covered by the docs-lint workflow. Removing the push filter would only spend runner minutes on changes that cannot break the build.

## Ruleset (maintainer, in the GitHub UI)

- Target default branch `main`, enforcement Active; require status check `ci-ok`; require branches up to date: off; require a pull request before merging: on, 0 approvals; bypass: repository admins only.

## Rollout note

The agent-skills apply card `t_83462076` (PR #79, merged 2026-10-17) predates this job, so `ci-ok` has no check run on that PR's head. If the board's completion contract only accepts checks that ran on the head, `t_83462076` closes against the four leg runs as a documented one-off; from this change's own PR (which carries `ci-ok` on its head) onwards, `ci-ok` is the gate for every PR.

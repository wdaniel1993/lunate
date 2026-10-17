# Tasks — ci-aggregate-gate

## 1. Implementation

- [ ] 1.1 `ci-ok` aggregate job in `.github/workflows/ci.yml` (`needs`, `if: always()`, explicit per-result check).
- [ ] 1.2 Audit + record: no path filters on `pull_request` (push-only `paths-ignore` stays).
- [ ] 1.3 `ci-pipeline` delta: "Aggregate merge gate" requirement + three scenarios.

## 2. Gate and roll

- [ ] 2.1 `scripts/verify.sh` green; PR; CI green including the new `ci-ok` check on the PR itself.
- [ ] 2.2 Merge; tell the maintainer `ci-ok` is on `main`; he enables the ruleset; then close `t_83462076` (head-check caveat per design).

## 1. Resolve the existing primary-number collision

- [ ] 1.1 Obtain maintainer direction in #339 for preserving the immutable accepted ADR records that both claim 0027; do not implement an exemption or rewrite either record without that direction.

## 2. Implement ADR number verification

- [ ] 2.1 Add a dependency-free filename checker for primary ADRs, excluding only `NNNN-addendum-*.md`, and report duplicate numbers, missing numbers, both relevant repo-relative paths, and `highest + 1` as the next free number.
- [ ] 2.2 Add exactly three fixture cases: duplicate primary numbers, a gap, and a clean consecutive set; assert failure/success and the required diagnostic details, with paths normalized consistently across platforms.
- [ ] 2.3 Integrate the live-tree check and its three-fixture self-test into `scripts/verify.sh --profile docs` / the existing `verify-docs` job without adding a build or test-suite step.

## 3. Document and verify

- [ ] 3.1 Add the numbering rule and explicit addendum convention to `docs/guide.md` so the next ADR author finds it; no ADR README exists.
- [ ] 3.2 Run `bash scripts/verify.sh --profile docs` and `openspec validate --all --strict`; both must pass after the maintainer-approved 0027 resolution.

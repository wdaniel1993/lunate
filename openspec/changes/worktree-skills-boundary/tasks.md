# Tasks — worktree skills boundary

- [ ] **Scanner fix.** `projectBoundary: request.WorktreeRoot` at both sites; the `ProjectRoots` stop rule per D2.
- [ ] **Regression tests.** Real `git worktree add` discovery test (worktree's own skill found; main-checkout-only skill absent; nothing above the worktree root read); unit cases for the stop rule.
- [ ] **Spec delta.** MODIFIED "Skill discovery" — linked-worktree bound + scenarios; originals preserved.
- [ ] **Docs check.** The boundary is spec-only (no docs surface describes it); confirm, then no docs change.
- [ ] **Gate + review + PR.**

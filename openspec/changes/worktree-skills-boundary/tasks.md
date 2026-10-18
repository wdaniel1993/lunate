# Tasks — worktree skills boundary

- [x] **Scanner fix.** `projectBoundary: request.WorktreeRoot` at both sites; the `ProjectRoots` stop rule per D2.

  Implementation note (design/code divergence, smallest-surface choice): the boundary is derived from the walk's own stop — `ProjectBoundary(worktreeRoot, repoRoot)` returns the repository root while it is an ancestor of the worktree root, the worktree root itself otherwise — instead of handing `request.WorktreeRoot` literally to both scan sites as D1 words it. `Workspace.WorktreeRoot` is the session's working directory, so on a main checkout started in a subdirectory it is a strict descendant of `RepoRoot`; the literal D1 value would reject the repository root's own skills and break the preserved scenario "A skill at the repository root is found from a subdirectory" (the existing subdirectory test). In a linked worktree both rules coincide — the boundary is the worktree root itself, which is what D1 is after — and `WorktreeRoot == RepoRoot` checkouts are unchanged.
- [x] **Regression tests.** Real `git worktree add` discovery test (worktree's own skill found; main-checkout-only skill absent; nothing above the worktree root read); unit cases for the stop rule.

  Red/green note: `A_linked_worktrees_own_skill_is_discovered`, `Nothing_above_a_linked_worktrees_root_is_read` and the git-free `A_repository_root_that_is_not_an_ancestor_does_not_extend_the_walk` fail before the fix (every candidate skipped as "outside the repository"; the walk escapes above the worktree root). `The_main_checkouts_skills_are_not_discovered_from_a_linked_worktree` also passes before it — the pre-fix bound scans neither tree — so it is the guard that the decided behavior (D3) stays.
- [x] **Spec delta.** MODIFIED "Skill discovery" — linked-worktree bound + scenarios; originals preserved.

  Landed with the proposal commit `bb35108`; all four original scenarios kept, three added (`openspec validate worktree-skills-boundary --strict` → valid).
- [x] **Docs check.** The boundary is spec-only (no docs surface describes it); confirm, then no docs change.

  Confirmed: `docs/guide.md`, `docs/architecture.md`, `docs/README.md` and `docs/spec/extensibility.md` carry no project-discovery-boundary statement (the extensibility spec defers to the `skills` capability spec). The one prose mention outside the specs is `adr/0017-addendum-skills.md` point 1 — "from the worktree root upward to the repository root (never above it)" — which the linked-worktree rule does not contradict (the main checkout is not above the worktree root) and which is immutable once accepted (`docs/README.md`); this change's impact names no ADR. No docs change.
- [ ] **Gate + review + PR.**

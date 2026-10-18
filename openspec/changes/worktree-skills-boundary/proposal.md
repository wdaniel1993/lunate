# Worktree skills boundary

## Why

A session whose workspace is a linked git worktree discovers **zero** project skills. `SkillScanner` bounds project discovery by `request.RepoRoot ?? request.WorktreeRoot`, and for a linked worktree the detected `RepoRoot` is the **main checkout** — the worktree's own files are never inside that path, so the boundary guard skips every candidate (`outside the repository; it was skipped`). The same wrong bound makes the upward walk miss its stop (the repository root is not an ancestor of the worktree root), so discovery also climbs toward the filesystem root, scanning unrelated ancestors.

Worktree sessions are a first-class scenario (ADR-0021 per-run workspaces; this repository's own agent runs), and the bug makes the local gate red in a worktree — every project skill is skipped, no trust prompt appears, and the production-composition test times out.

## What changes

- The project discovery boundary becomes the **worktree root** (the session's workspace). Identical to today on main checkouts, where `WorktreeRoot == RepoRoot`.
- The upward walk stops at the repository root only when it is an ancestor of the worktree root; in a linked worktree, discovery is bounded to the worktree root itself and the main checkout is not scanned.
- Regression tests run discovery against a **real linked worktree** (`git worktree add` in a temp repository), which the existing tests never did.

## Impact

- `src/Lunate.Coding/SkillScanner.cs` only; no public surface change.
- `openspec/specs/skills/spec.md` — the discovery requirement gains the linked-worktree bound (MODIFIED).

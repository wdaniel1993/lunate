# Design — worktree skills boundary

## D1. Boundary := the worktree root

The boundary guard exists so a skill directory is followed only inside the session's own tree (ADR-0016). `RepoRoot` is repository **identity** (trust keying; ADR-0021) — for a linked worktree it names the main checkout, which is not the session's tree. The boundary therefore becomes `request.WorktreeRoot` at both scan sites (`Scan` and `ScanProject`). On main checkouts `WorktreeRoot == RepoRoot`, so behavior there is unchanged.

## D2. The walk stops at the session's tree

`ProjectRoots` walks from the worktree root upward, stopping at the repository root. When the repository root is not an ancestor of the worktree root (a linked worktree), the stop never matches and the walk escapes toward the filesystem root. The stop is now the repository root **only when `IsInside(worktreeRoot, repoRoot)`**; otherwise the walk stops at the worktree root itself. This drops the main checkout from the scan roots in worktree sessions, as decided (its files are outside the session's workspace).

## D3. The regression test uses a real linked worktree

`git worktree add` against a temp repository; discovery must find the worktree's own `.agents/skills` skill, must not include a skill that exists only in the main checkout, and nothing above the worktree root may be read. Unit cases keep covering main-checkout and subdirectory behavior.

## SIGN-OFF (Daniel, 2026-10-18, card t_7bb725c9)

Verbatim decision: *"Boundary := the worktree root; drop the main checkout from worktree sessions"* (option 1 of the card's two options).

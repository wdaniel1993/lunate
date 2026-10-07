# add-workspace-roots

## Why

ADR-0017 ("Workspaces and repository identity") pins the workspace object, and the worktree addendum's landing map assigns it here: the run's workspace must expose `WorktreeRoot`, `RepoRoot` and `GitCommonDir` so tools, sessions and (later) background services share one identity, and the boundary must be provably per-worktree. Today `Workspace` (T-12, ADR-0016) knows only a working directory and extra roots.

## What Changes

- `Workspace` gains repository identity: `WorktreeRoot` (the run's canonical checkout), `RepoRoot` (the main worktree's root, for display) and `GitCommonDir` (the shared git directory), detected from the file system only — a `.git` directory, or the `gitdir:`/`commondir` files of a linked worktree — never by running git.
- Boundary semantics are pinned per worktree: a run in worktree A cannot touch worktree B unless B is explicitly granted as an extra root (the existing "unless allowed" mechanism).
- Run plumbing: the identity is the single source consumed by the run's parts — file tools' boundary (this change), session header and grouping (schema 2, `add-session-schema-v2`), and the future `bash` tool card, whose cwd is `WorktreeRoot` (documented constraint here, implemented there).
- Spec delta: `agent-files` "Workspace boundary" gains the identity and cross-worktree scenarios.

## Impact

- `Lunate.Coding`: `Workspace` (+ tests). No harness changes, no new dependencies, no golden changes; existing boundary behaviour is unchanged.
- Docs: the design notes the bash-card constraint; nothing else changes.

# Design — add-worktree-support

## Where each piece lands

| Piece | Home |
| --- | --- |
| Workspace object (`WorktreeRoot`, `RepoRoot`, `GitCommonDir`), run plumbing, boundary across worktrees, bash cwd | `add-workspace-roots` (next-but-one change; tests: run in worktree A cannot write to worktree B unless allowed) |
| Session header records repository identity + worktree path; sessions grouped by repository, distinct per worktree | `add-session-schema-v2` (schema 2; tests: grouping) |
| Trust keyed by repository identity; extension content-hash per worktree | T-36 (trust prompt) |
| Per-workspace services keyed by `WorktreeRoot`, Roslyn cap (two loaded, LRU), `FileChanged` carries workspace id | T-39 (services) with T-25 (Roslyn); tests: cap and unload |
| Approval "outside tracked files" uses the worktree index; AGENTS.md/settings discovery from the worktree root | T-16 (config) and T-21 (approval UI) |
| Restore hint for fresh worktrees (ADR-0006) | T-25 (Roslyn operational rules); test: fresh worktree gets the hint, not an avalanche |
| Worktree management (create/remove, branch naming, merge-back, conflicts, cleanup, `/worktree`) | extension territory — the `worktree-tasks` sample (T-51) proves it |

## Decisions pinned here

- **Repository identity** = `GitCommonDir` (the shared `.git` across worktrees), falling back to the remote URL when available. `RepoRoot` is the main worktree's root for display; `WorktreeRoot` is the run's checkout — boundary checks and `bash` use `WorktreeRoot`.
- **Session grouping**: `~/.lunate/sessions/` groups by a hash of the repository identity; sessions are distinct per worktree path; the header carries both fields (schema 2 addition — coordinated with `add-session-schema-v2`).
- **Trust**: one trust decision per repository identity; the per-worktree content-hash check remains for project extensions.
- **Services**: keyed by `WorktreeRoot`; Roslyn workspaces capped (default two, LRU unload); `FileChanged` payload gains the workspace id.
- **Restore hint**: on load failures caused by missing restore output in a fresh worktree, the message is actionable ("run `dotnet restore` in <path>", or offer to run it).

## Deliberate non-goals

- No worktree creation/merge/cleanup in core (extension territory; proven by the T-51 sample).
- No `src/` changes in this change; the two follow-up changes carry the code.

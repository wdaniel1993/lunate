# Design: add-workspace-roots

## Context

`Workspace` (T-12) canonicalizes paths, resolves symlinks through raw targets, bounds link chains, and accepts only canonical targets under an allowed root (the working directory plus extra roots; case rules per platform). ADR-0017 and the worktree landing map add identity and per-worktree boundaries. No new dependencies, no golden changes; implements an accepted decision, so no new ADR.

## Decisions

### Identity detection (file system only)

From the canonical `WorktreeRoot`:

- `.git` is a **directory** → `GitCommonDir` = it; `RepoRoot` = `WorktreeRoot` (the main checkout).
- `.git` is a **file** (linked worktree) → read its first line, `gitdir: <path>`, resolved against the file's directory and canonicalized; then read `<gitdir>/commondir` (single line, resolved against `gitdir`, canonicalized) → `GitCommonDir`; `RepoRoot` = the parent of `GitCommonDir` when its name is `.git`, else null.
- No `.git` → all three identity members are null (non-repository workspace).

Rules: detection is **best-effort identity, not validation** — malformed `.git`/`commondir` content yields nulls, never throws (a workspace must always work). Git is **never executed**. All reported paths are canonicalized with the existing `Canonicalize`. `WorktreeRoot` = the canonical working directory (existing behaviour, exposed under its architecture name).

### Boundary

Mechanics are unchanged (ADR-0016). Allowed roots = `WorktreeRoot` + extra roots; the comparison rules stay per platform. Worktrees of one repository are separate boundaries; granting another worktree (or any directory) as an **extra root** is the explicit "unless allowed" — one run's default reach never crosses into a sibling worktree.

### Consumers (run plumbing)

- File tools: constructed with the workspace; boundary per `WorktreeRoot` (this change's tests prove A-refuses-B).
- Sessions: the host passes `worktree: WorktreeRoot` and `repo: GitCommonDir` (or, when it has one, a remote URL — resolving remotes is a host concern; reading `.git/config` correctly, with includes, is deliberately out of scope) to `Session.Create` / `SessionPaths.ForRepository` (schema 2).
- Background services (T-39) key by `WorktreeRoot` — enabled by this object, wired there.
- `bash` (future card): its working directory SHALL be `WorktreeRoot`. No bash tool exists yet; the constraint is pinned here so the card cannot drift.

### Non-goals

- No git subprocesses, no `.git/config` parsing, no worktree creation/removal (extension territory, T-51).
- No changes to `Lunate.Agent` (sessions already accept the strings; the harness stays identity-agnostic).

## Testing strategy

- Main checkout: identity = root / root / root/.git.
- Linked worktree: fabricated on disk without git (`.git` file with `gitdir:`, `commondir` file) → `WorktreeRoot` = the worktree, `RepoRoot` = the main root, `GitCommonDir` = the shared `.git`; canonicalized (symlinked root case included).
- Non-repository: nulls; boundary still works.
- Malformed `.git`/`commondir` → nulls, no throw.
- Cross-worktree: resolve a path in worktree B from a workspace rooted at A → refused naming the target and allowed roots; with B granted as an extra root → accepted.
- All existing T-12 boundary tests stay untouched and green (behaviour unchanged).

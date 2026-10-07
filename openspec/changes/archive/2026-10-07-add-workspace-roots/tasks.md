# Tasks

## 1. Workspace identity (TDD, red-first)

- [x] 1.1 `Workspace` exposes `WorktreeRoot` (canonical working directory), `RepoRoot`, `GitCommonDir` (nullable); detection from `.git` directory / `.git` file + `commondir` per design.md; git never executed
- [x] 1.2 Malformed `.git`/`commondir` content yields nulls without throwing; non-repository workspace yields nulls and keeps working
- [x] 1.3 Tests: main checkout; linked worktree (fabricated on disk, no git binary); non-repository; malformed; symlinked worktree root canonicalized

## 2. Boundary across worktrees

- [x] 2.1 Tests: a path in worktree B resolved from a workspace rooted at A is refused (naming target and allowed roots); granted as an extra root it is accepted
- [x] 2.2 Existing boundary tests untouched and green (behaviour unchanged)

## 3. Close

- [x] 3.1 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-workspace-roots --type change --strict`
- [x] 3.2 Self-review; commit per group

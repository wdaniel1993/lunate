---
name: openspec-git-discipline
description: Use when running OpenSpec propose, continue, apply, verify, archive, or worktree workflows where proposal artifacts, branches, merges, or archive timing affect git history.
license: MIT
compatibility: Requires git and OpenSpec workflow artifacts.
---

# OpenSpec Git Discipline

## Core Rule

Every OpenSpec state change must cross `main` before the next lifecycle phase depends on it.

- Propose/continue artifacts may be drafted on a branch, but must be committed and merged to `main` before apply starts.
- Apply may run on `main`, a branch, or a worktree only if that exact proposal change is already available on `main`.
- Archive is the **final commit of the implementation PR**: run it on the change branch once review is done and the fast gate is green, then merge. Main receives code and specs in the same merge.

Never create commits, branches, or merges unless the user explicitly asks.

## Gates

| Moment | Gate |
| --- | --- |
| Before propose | Prefer `main`; if not, warn and ask whether to continue intentionally. |
| During continue | Before creating the next artifact, ask the user to commit completed artifact changes or explicitly continue without that checkpoint. |
| After propose | Ask the user to commit proposal artifacts; offer to create a PR branch for review. |
| Before apply | Confirm the proposal change is committed on `main`; then apply may run from `main`, a branch, or a worktree. |
| Before archive | Review is done and the fast gate is green on the change branch. Run `openspec archive <change> --yes` as the final commit of that same PR. |
| After archive | Push the archive commit and merge once `ci-ok` is green. Never open a separate archive PR, and never leave more than one archive pending. |

## Required Checks

Before apply:

1. Run `git status --short`.
2. Verify `openspec/changes/<change>/` has no uncommitted proposal files.
3. Verify the proposal change exists on `main` before applying from any branch/worktree.

Use this language if the proposal has not reached `main`:

> I should not apply this yet because the proposal change has not reached `main`. A proposal can be drafted on a branch, but apply must start only after that proposal state is available on `main`. Please merge or commit the proposal to `main` first, then I can apply from `main`, a branch, or a worktree.

Before archive:

1. Run `git branch --show-current` and `git status --short`.
2. Stop if not on `main`.
3. Stop if implementation work has not been merged back to `main`.

Use this language:

> I should not archive this yet because archive must run from `main` after implementation is merged back. Verify makes a change eligible to merge; it does not replace the merge.

## Red Flags

- Applying a proposal that exists only on the current branch/worktree.
- Treating worktree visibility as proof that the proposal reached `main`.
- Creating the next continue artifact without asking about committing the previous one.
- Archiving from a feature branch or before implementation is merged to `main`.
- Auto-committing, branching, or merging without explicit user approval.

All of these mean: pause, explain the boundary, and ask the user to make the git state explicit.

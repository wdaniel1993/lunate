# Bash change marks (git status) — proposal

## Why

The bus's signed-off limit (D9 of `wire-file-change-bus`): bash mutations are not published, so a same-stamp bash edit is only seen at the next reload. The follow-up card (from that sign-off) asks to close it: after bash, mark changed files exactly via `git status`, through the same path the bus marks tool edits.

## What (proposed, pending sign-off)

`BashTool` runs a single `git status --porcelain -z --relative --no-optional-locks` after each command that ran, and publishes every changed path through `ctx.FileChanges` (the `IFileChangeSink` tools already use). A new `GitChangeScanner` owns the invocation + parsing; failures are silent.

## Decisions pending

See `design.md` — Q1 trigger (every bash call in a git worktree, recommended), Q2 publish scope (all changed paths, recommended), Q3 untracked (files yes, directories no), Q4 timeout/cancel (scan on timeout, skip on cancel), Q5 deletions/renames (both published).

## Status

Design-first: this draft lands no code. On sign-off the change is applied (scanner + tool wiring + tests + spec deltas), gated and PR'd as usual.

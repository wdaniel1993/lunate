# Tasks: path completion and file index (T-53)

## 1. Ignore engine + index core (Coding)

- [x] 1.1 `GitIgnore` pattern parser + regex translation + evaluator (comments, negation, dir-only, anchoring, `*`/`**`/`?`/classes, last-match-wins, deeper-file-wins)
- [x] 1.2 `IWorkspaceFiles` seam + `SystemWorkspaceFiles` walk (relative entries, no symlink descent, `.git` skipped)
- [x] 1.3 `FileIndex`: lazy async build, sorted+ordinal entries, 200,000 cap + truncation flag, prefix query (dirs trailing `/`), thread-safe publication
- [x] 1.4 Tests: ignore matrix (per pattern class + nested + negation + dir prune), cap/ordering/truncation via the seam, laziness (nothing built before first use)

## 2. Session wiring

- [x] 2.1 `ApplyCompletion`: `@token` branch (cursor-at-token-end scan, indexing notice, completion + `@` re-prefix, candidates notice capped at 20 with `… (+N more)`); slash behaviour unchanged
- [x] 2.2 Tests: token scan cases (mid-text, cursor not at end, no `@`), single/multi/no-progress completion, indexing notice path, session-level flow with a fake index/real temp tree

## 3. Budget step

- [x] 3.1 `PathIndexBudgetTests` (`Category=Perf`): 20k-file tree, build ≤ 5 s, query ≤ 50 ms, prints measurements, cleans up
- [x] 3.2 `verify.sh` + `verify.ps1`: exclude `Category=Perf` from both main passes; new "path index budget" step running the trait
- [x] 3.3 Calibrate: confirm the budget holds locally and note measured values in the report

## 4. Gate

- [x] 4.1 `bash scripts/verify.sh` green (incl. the new step); deviations recorded in design.md

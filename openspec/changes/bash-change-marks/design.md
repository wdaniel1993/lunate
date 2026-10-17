# Bash change marks (git status) — final design (approved 2026-10-17)

## Problem

The file-change bus's documented limit (D9 of `wire-file-change-bus`): **bash mutations are not published**. Tool edits are exact via the bus; bash/external edits are caught by the content-stamp scan; a same-stamp change outside the tool path is seen only at the next reload. This change closes that limit for bash.

## Core: the publisher diffs against the previous scan

`git status` answers "what differs from HEAD", **not** "what did this call change". Publishing raw status would re-flag every already-dirty file after every bash call (the `t_6fcf625a` symptom: endless re-flagging), and would miss clean→clean changes. So the publisher keeps a **per-run snapshot** — HEAD oid + `{path → content hash}` for every path in the last status set — and after each bash call publishes a path only when:

- **(a)** it is in the current status set and is **new or its content hash differs** — catches same-stamp edits to already-dirty files (the D9 case) and genuinely changed files;
- **(b)** it was in the previous set but is **no longer in the current one, with content different from the cached hash** — a revert (`checkout --`, `reset`). A **commit leaves content unchanged → skipped**;
- **(c)** it is in `git diff --name-only <oldHEAD> <newHEAD>` when **HEAD moved** (`checkout`, `pull`, `rebase`, `stash pop` with commits).

Only status-listed paths are hashed — the set is small, and that is the cost model. The snapshot is taken lazily at a run's first bash call (equivalent to run start; the first call then has a baseline), keyed by `ctx.RunId` + the run's workspace root.

## Decisions (approved with adjustments)

- **Q1 trigger** — every bash call, gated on `workspace.GitCommonDir is not null`. The scan runs **from the run's workspace root** (`ctx.WorkingDirectory`), not the bash tool's cwd (commands may `cd`; per-run workspaces / child worktrees differ). `git --no-optional-locks status` (or `GIT_OPTIONAL_LOCKS=0`) so we never take `index.lock` and race the user's or the command's own git operations. The git call gets its **own ~2 s timeout**; on timeout: log and skip — the stamp scan stays the backstop. Non-git workspace: no change, stamp scan only.
- **Q2 scope** — publish all changed paths, subscribers filter — applied to the **diffed set** above, never raw status. Events are **tagged `source = bash`**.
- **Q3 untracked** — untracked files published, untracked directories skipped. A test proves a new `.cs` inside a new untracked directory still reaches `cs_diagnostics`: new files are not documents until a load, so the test pairs the file with a project change the same bash call publishes, and the reload brings it in. `--ignore-submodules=dirty`. `.gitignore`d files (`bin`/`obj`, generated output) are out of scope — stated in the spec.
- **Q4 timeout/cancel** — scan after success, non-zero exit **and user cancellation** (Esc on a long command may leave half-written files and the run continues) — with a short **independent** timeout, not the cancelled token. Only run/session teardown skips (a best-effort scan under teardown silently no-ops; the scanner never throws).
- **Q5 deletions/renames** — deletions publish a `Modified` mark (the backend's mark path already handles missing files); renames publish both paths. Note: a plain `mv` shows up as **delete + untracked**, not as a rename entry (only staged renames do) — both shapes get a test. Status kinds: `??`/`A` → `Created`, everything else → `Modified`.

## Mechanics

- One `git status --porcelain -z --relative --no-optional-locks --ignore-submodules=dirty -unormal` per scanned call (cwd = run workspace root; `--relative` yields root-relative paths in one invocation and excludes paths outside the root). NUL-separated parsing; rename entries carry two paths.
- Content hash: the repository's content-hash helper (same algorithm as the trust/extension pins).
- HEAD tracking: read `git rev-parse HEAD`; when it moved, `git diff --name-only <old> <new>` feeds rule (c), then the snapshot's HEAD updates.
- Event shape mirrors `EditTool`: `new FileChangeEvent(absolute, relative, ctx.WorkingDirectory, ctx.RunId, kind)` plus `Source = "bash"`; the run sink stamps workspace/run ids as for tool edits; the bus forwards the source onto `FileChangedPayload`.
- Failure discipline: missing git, non-repo root, scan timeout or parse trouble → no events, never an exception into the tool path; the stamp scan remains the backstop.
- Cost (measured 2026-10-17): one status + hashing the status set per bash call in git worktrees — ~0.01 s on the project repo, 0.19–0.52 s on a larger repository (~/.hermes/hermes-agent; the first run is the cold one). Bash calls are seconds-scale; acceptable, with the design's own timeout as the guard.

## Spec deltas (at apply)

- `agent-tools` MODIFIED ("C# diagnostics tool"): the files-changed definition gains bash-marked changes (diffed publisher); scenarios — a same-stamp bash edit seen without a reload; an already-dirty file is not re-flagged by an unrelated bash call; a cancelled call is still scanned.
- `extensions` MODIFIED (bus limit sentence): "bash mutations are not published" becomes the git-status diffed-publish rule (git worktrees; untracked files included, untracked directories and `.gitignore`d files excluded; submodule dirty state ignored; scan timeout falls back to the stamp scan). Original scenarios preserved verbatim; new scenarios for the bash mark + revert/commit behaviour.

## Tests

- **Scanner/tracker unit** (canned porcelain + real-repo fixtures): parse (renames, untracked, deleted); rule (a) new/changed; **same-stamp edit to a dirty file caught**; **already-dirty file NOT re-flagged on an unrelated call**; rule (b) **`git checkout -- f` revert caught**, **`git commit` not flagged**; rule (c) **branch switch flags the diffed files**; untracked file yes / untracked dir skipped; plain `mv` = delete + untracked; staged rename both paths; non-repo root → nothing; workspace-as-subdirectory (`--relative` mapping).
- **BashToolTests**: a recording sink receives the right tagged events after commands; sink/scanner failures never fail the tool; non-git workspace emits nothing; **a cancelled call is still scanned** (independent timeout).
- **Integration (real Roslyn, Coding.Tests)**: a same-stamp bash edit → the next `cs_diagnostics` reports the new state **without a reload** (the residual limit closed); bash mark + stamp scan idempotent (one changed-file count); new `.cs` in an untracked directory → the reload a project change triggers brings it in (asserted at solution scope — a reload re-baselines the changed scope).

## Non-goals

- No `FileSystemWatcher`; no marks for non-bash tools (already exact); stamp-scan backstop unchanged (external editors); no per-extension hooks; no `-uall`; no index-lock interaction.

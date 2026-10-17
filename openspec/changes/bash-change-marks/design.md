# Bash change marks (git status)

## Problem

The file-change bus's documented limit (sign-off D9 of `wire-file-change-bus`): **bash mutations are not published**. Tool edits are exact via the bus; bash/external edits are caught by the content-stamp scan; a **same-stamp change made outside the tool path is seen only at the next reload** (the scan cannot distinguish it). This change closes that residual limit for bash: after a bash call, detect changed files via `git status` and publish them through the same `IFileChangeSink` path tool edits use.

## Change (proposed)

- `BashTool` scans the workspace after a command ran (success, non-zero exit, timeout) and publishes one `FileChangeEvent` per changed path via `ctx.FileChanges` — the exact path EditTool/WriteTool use.
- New `GitChangeScanner` (Lunate.Coding): runs `git status --porcelain -z --relative --no-optional-locks` with the workspace root as cwd, parses the NUL-separated output, maps paths to canonical absolute + workspace-relative pairs, and returns the change set. Errors are silent (never fail the bash result).
- Scope and edge cases per the decisions below.

## Decisions (sign-off requested)

- **Q1 trigger** — recommended: every bash call, gated on `workspace.GitCommonDir is not null` (a cheap startup-known fact; no new seams). Alternatives: gate on a live Roslyn backend (cheaper in non-C# sessions, but couples `BashTool` to the Roslyn registry and starves non-Roslyn bus subscribers); gate on root-level `.sln`/`.csproj` presence (static, misses nested projects).
- **Q2 publish scope** — recommended: **every changed path**; the bus delivers per subscriber pattern, exactly as for tool edits (the Roslyn subscriber already filters `.cs` vs reload triggers). Pre-filtering in the publisher would duplicate the subscriber's filter and starve other subscribers.
- **Q3 untracked** — recommended: `-unormal`; publish untracked **files** (`??` without trailing slash) but skip untracked **directories**. Rationale: new files are stamp-visible (fresh mtime — the scan already catches them); only same-stamp edits need the exact mark, and those are tracked-file modifications. `-uall` on a repo with big untracked trees is pathologically expensive.
- **Q4 timeout/cancel** — recommended: scan after success, non-zero exit and timeout; **skip on cancellation** (run teardown; the next session's first load reads disk). Alternative: best-effort scan on the cancel path (more surface for a marginal case).
- **Q5 deletions/renames** — recommended: publish deletions as `Modified` (the backend's mark path already handles missing files — the refresh flags the deletion); renames publish both paths (new = `Created`, old = `Modified`). Status kinds: `??`/`A` → `Created`, everything else → `Modified`.

## Mechanics

- One git invocation per scanned bash call: `git status --porcelain -z --relative --no-optional-locks` (cwd = workspace root, timeout 10 s, output cap consistent with the tool's capture). `--relative` yields workspace-root-relative paths in one call (no `rev-parse` second spawn) and inherently excludes paths outside the root.
- NUL-separated parsing (`-z`) — no quoting ambiguity for exotic paths; rename entries carry two paths (publish both).
- Event shape mirrors `EditTool`: `new FileChangeEvent(absolute, relative, ctx.WorkingDirectory, ctx.RunId, kind)`; the run sink stamps the run's workspace/run ids as for tool edits.
- The scanner never throws into the tool path; a missing git binary, a non-repo root or a scan timeout means "no events".
- Cost: one `git status` per bash call in git worktrees. Bash calls are seconds-scale; the scan is tens of milliseconds. A task measures it on a large repo and records the number; if it ever hurts, a future knob can gate it — not in v1.

## Spec deltas (at apply)

- `agent-tools` MODIFIED ("C# diagnostics tool"): the "files changed since the last check" definition gains bash-marked changes; new scenarios — a same-stamp bash edit is seen by the next `cs_diagnostics` without a reload; a non-git workspace is unaffected.
- `extensions` MODIFIED (the file-change-bus limit sentence): "bash mutations are not published" becomes "bash mutations are published exactly via the git-status scan after each bash call (git worktrees; untracked files included; untracked directories and cancelled runs excluded)". Original scenarios preserved verbatim; new scenario for the bash mark.

## Tests (at apply)

- Unit: scanner parse (canned porcelain incl. renames/untracked/deleted); real-repo fixtures (modified same-stamp, added, deleted, renamed, untracked file, untracked dir skipped); non-repo root → no events; workspace-as-subdirectory (`--relative` mapping).
- `BashToolTests`: a recording sink receives the right events after `echo x > a.cs`-style commands; sink errors never fail the tool; non-git workspace emits nothing.
- Integration (real Roslyn): bash edit with a reset timestamp (same stamp) → next `cs_diagnostics` reports the new state **without a reload** (the residual limit closed); bash mark + stamp scan idempotent (one changed-file count).

## Non-goals

- No `FileSystemWatcher`; no marks for non-bash tools (already exact); no change to the stamp-scan backstop (it stays for external editors); no per-extension hooks.

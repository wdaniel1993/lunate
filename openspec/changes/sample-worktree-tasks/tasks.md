# Tasks — sample-worktree-tasks

## 1. Child-run workspaces (`Lunate.Agent`, TDD)

- [x] 1.1 `GitIdentity.Detect(root)` extracted (never executes git; `.git` dir or file + `commondir`); Coding's `Workspace` delegates to it (no behavior change; Coding workspace tests stay green).
- [x] 1.2 `SubagentRequest.WorkspaceRoot` (canonicalized; null inherits); refusals as teaching error results: `workspace_missing` (missing/non-directory, naming the path) and `workspace_outside_repository` (root not a worktree of the same repository — same git-common-dir); parent-root-named behaves as inherit.
- [x] 1.3 `AgentHarnessOptions.WorkspaceTools` factory seam; child composition: another root → `WorkspaceTools(root)` (allow-list applies to that set; missing seam → `workspace_tools_unavailable` refusal); no root/parent root → the parent's instances exactly as today; project MCP tools excluded from a different-root child unless their server is marked `workspaceIndependent`; the parent session's loaded extensions are used as-is (no rediscovery, no mid-run trust prompts).
- [x] 1.4 Child session header records the child's own repo/worktree when the root differs; mirrors the parent's otherwise; file stays beside the parent's; the child keeps the parent's system prompt verbatim (no AGENTS.md re-discovery).
- [x] 1.5 `ApprovalRequest` + `ApprovalRequested` carry the asking run's workspace root; the TUI prompt shows the child root (and the ACP mapping carries it); tests pin the label.
- [x] 1.6 Agent tests: workspace child edits isolated; cross-worktree write refused; header; refusals; allow-list against the child's own set; approval label; inherit path unchanged (existing T-47 suites stay green).

## 2. Per-workspace services + Roslyn wiring (`Lunate.Coding`, `Lunate.Roslyn`)

- [x] 2.1 `WorkspaceServices`: canonical-root keyed, lazy, thread-safe; loaded-workspace cap `roslyn.maxLoadedWorkspaces` (default 2, LRU unload + dispose); in-flight calls pin their workspace (never unload under a call; all-pinned → cancellation-aware wait); observable eviction for tests; invalid values refused with a teaching error naming the key; memory documented (up to 2× the ADR-0009 budget) and included in the memory measurement notes.
- [x] 2.2 `HarnessFactory` composes the five `cs_*` tools into every run (all modes) bound to the run's workspace through the registry, offered only where the workspace has `.sln`/`.slnx`/`.csproj` (cheap detection, no Roslyn); child runs in a worktree get them bound to the child root; laziness preserved (no Roslyn load before first call; startup budget gate stays green).
- [x] 2.3 Tests: two roots load two workspaces; third root evicts the LRU victim (assert the root); touch-before-load keeps the touched one; in-flight call pins (cap waits); fresh worktree returns the restore hint; no-solution workspace offers no cs tools; SDK-absent path unchanged.
- [x] 2.4 MCP `workspaceIndependent` (optional bool, default false) parsed from `mcp.json` entries; exclusion test in the child composition.

## 3. Sample (`samples/extensions/worktree-tasks`)

- [ ] 3.1 Extension: `worktree__start` / `__merge` / `__clean` / `__list` over `core/process` + `ctx.Subagents` (start spawns one child with `WorkspaceRoot` and a fixed allow-list; name sanitization + `git check-ref-format --branch` + refuse-existing-branch; merge refuses dirty target and `--abort`s conflicts; clean refuses while a run is active and deletes the branch only if merged); annotations; README with the capability table.
- [ ] 3.2 Kit tests against real temp git repos: registration; start (worktree + branch + child in the worktree + file isolation + child header); merge (arrives as merge commit; dirty refusal; conflict → `--abort` + teaching error naming files); clean (removes; dirty refusal; active-run refusal; merged-only branch delete); list; bad names refused.

## 4. Coding-level proofs + trust

- [ ] 4.1 Two subagents in two worktrees (scripted): isolated edits, both child sessions beside the parent's with their own headers, both event streams forwarded.
- [ ] 4.2 Roslyn per workspace + cap proof (2.3 shape, end to end through the sample flow).
- [ ] 4.3 Trust by repository identity: new worktree of a trusted repository does not re-prompt; content-hash stays per worktree (Extensibility.Tests; product change only if the store misbehaves).
- [ ] 4.4 Wiring: solution entries; layering edges; gate stays green.

## 5. Verify, review, archive

- [ ] 5.1 `scripts/verify.sh` green (both cultures); openspec strict; csharpier/style/analyzers/markdownlint clean.
- [ ] 5.2 Adversarial review; fix findings; focused re-review.
- [ ] 5.3 Full short ADR `adr/0021-per-run-workspaces.md` ("per-run workspaces and per-workspace services", amending 0006/0016/0017); archive; push; PR.

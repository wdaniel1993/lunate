# Tasks — sample-worktree-tasks

## 1. Child-run workspaces (`Lunate.Agent`, TDD)

- [ ] 1.1 `GitIdentity.Detect(root)` extracted (never executes git; `.git` dir or file + `commondir`); Coding's `Workspace` delegates to it (no behavior change; Coding workspace tests stay green).
- [ ] 1.2 `SubagentRequest.WorkspaceRoot` (canonicalized; null inherits); refusal `workspace_missing` as a teaching error result naming the path; parent-root-named behaves as inherit.
- [ ] 1.3 `AgentHarnessOptions.WorkspaceTools` factory seam; child composition: another root → `WorkspaceTools(root)` (allow-list applies to that set; missing seam → `workspace_tools_unavailable` refusal); no root/parent root → the parent's instances exactly as today.
- [ ] 1.4 Child session header records the child's own repo/worktree when the root differs; mirrors the parent's otherwise; file stays beside the parent's.
- [ ] 1.5 Agent tests: workspace child edits isolated; header; refusals; allow-list against the child's own set; inherit path unchanged (existing T-47 suites stay green).

## 2. Per-workspace services + Roslyn wiring (`Lunate.Coding`, `Lunate.Roslyn`)

- [ ] 2.1 `WorkspaceServices`: canonical-root keyed, lazy, thread-safe; loaded-workspace cap `roslyn.maxLoadedWorkspaces` (default 2, LRU unload + dispose); observable eviction for tests; invalid values refused with a teaching error naming the key.
- [ ] 2.2 `HarnessFactory` composes the five `cs_*` tools into every run (all modes) bound to the run's workspace through the registry; child runs in a worktree get them bound to the child root; laziness preserved (no Roslyn load before first call; startup budget gate stays green).
- [ ] 2.3 Tests: two roots load two workspaces; third root evicts the LRU victim (assert the root); touch-before-load keeps the touched one; `cs_diagnostics` works in each worktree (fixture solution copied); SDK-absent path unchanged.

## 3. Sample (`samples/extensions/worktree-tasks`)

- [ ] 3.1 Extension: `worktree__start` / `__merge` / `__clean` / `__list` over `core/process` + `ctx.Subagents` (start spawns one child with `WorkspaceRoot` and a fixed allow-list); annotations; README with the capability table.
- [ ] 3.2 Kit tests against real temp git repos: registration; start (worktree + branch + child in the worktree + file isolation + child header); merge (arrives as merge commit; conflict → teaching error naming files); clean (removes; dirty refusal); list.

## 4. Coding-level proofs + trust

- [ ] 4.1 Two subagents in two worktrees (scripted): isolated edits, both child sessions beside the parent's with their own headers, both event streams forwarded.
- [ ] 4.2 Roslyn per workspace + cap proof (2.3 shape, end to end through the sample flow).
- [ ] 4.3 Trust by repository identity: new worktree of a trusted repository does not re-prompt; content-hash stays per worktree (Extensibility.Tests; product change only if the store misbehaves).
- [ ] 4.4 Wiring: solution entries; layering edges; gate stays green.

## 5. Verify, review, archive

- [ ] 5.1 `scripts/verify.sh` green (both cultures); openspec strict; csharpier/style/analyzers/markdownlint clean.
- [ ] 5.2 Adversarial review; fix findings; focused re-review.
- [ ] 5.3 `adr/0017-addendum-run-workspaces.md`; archive; push; PR.

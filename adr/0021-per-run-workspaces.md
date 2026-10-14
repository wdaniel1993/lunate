# ADR-0021: Per-run workspaces and per-workspace services

- Status: accepted — 2026-10-14 (maintainer sign-off: "full short ADR … amending 0006, 0016 and 0017")
- Context: `sample-worktree-tasks` (T-51; guide §Phase 6, fitness row `worktree-tasks`)
- Amends: ADR-0006 (restore/SDK failure handling), ADR-0016 (workspace boundary), ADR-0017 (extensibility architecture)

## Context

The extensibility architecture (ADR-0017, §Workspaces and repository identity) already pins that *every run — child runs included — has a workspace* and that *per-workspace services are keyed by `WorktreeRoot`, started lazily and capped (default: two loaded Roslyn workspaces, least-recently-used unloaded)*. The top-level half was built (workspace identity, session headers and grouping, trust keying); the child-run half and the services registry were not: `SubagentRequest` carried no workspace, `SubagentRunner` composed every child from the parent's working directory and tool instances, and nothing keyed or capped Roslyn backends. The fitness suite's `worktree-tasks` sample needs those primitives (two subagents in separate worktrees editing and building independently, with the cap holding), so this ADR records the decisions before they ship.

## Decision

**Child-run workspaces.**

- `SubagentRequest` gains `WorkspaceRoot` (the child run's worktree root; null inherits the parent's, naming the parent's own root behaves as the inherit case). The root is canonicalized and must be a worktree of the **same repository** (same git-common-dir); missing/non-directory and outside-repository roots are refused as teaching error results (`workspace_missing`, `workspace_outside_repository`), never exceptions — the established runner-refusal shape.
- The child's working directory and offered tool set follow the request's workspace. Tools for another root are built by a composer-supplied factory (`AgentHarnessOptions.WorkspaceTools`; Agent cannot construct Coding tools). Without the factory, a workspace spawn refuses (`workspace_tools_unavailable`) — never the parent's tools in the wrong directory. The allow-list applies to the child's actual set.
- The child keeps the parent's system prompt (no per-worktree AGENTS.md re-discovery), uses the parent session's loaded extensions as-is (no per-worktree rediscovery, no mid-run trust prompts), and **project MCP tools are not offered to a different-root child** unless their server is explicitly marked `workspaceIndependent` (their servers run in the parent's directory).
- Child session files stay beside the parent's (schema-2 rule) and their header records the **child's own** `repo`/`worktree` identity using the existing schema-2 fields — no schema bump; this ADR records the semantics.
- The approval seam carries the asking run's workspace root so a prompt can show where the call will act.

**Per-workspace services.**

- A registry keyed by canonical `WorktreeRoot` creates per-workspace services lazily and disposes them on eviction or shutdown. Roslyn backends are keyed by root; the loaded-workspace cap is `roslyn.maxLoadedWorkspaces` (default **2**), least-recently-used unloaded.
- **In-flight calls pin their workspace**: a pinned entry is never evicted; when every loaded entry is pinned, a new root's load waits (cancellation-aware) instead of unloading under a live call. Pinning is per call, so a long-lived child can still see its backend evicted between two of its own calls — acceptable and documented.
- The five `cs_*` tools are composed into every run (all modes, child runs included) through the registry, **offered only where the workspace looks like C#** (a `.sln`/`.slnx`/`.csproj` at the root; cheap file scan, no Roslyn load). Nothing loads before the first `cs_*` call.
- Memory: the cap makes up to 2× the ADR-0009 reference-workspace peak the documented worst case.

**Amendment notes.**

- ADR-0006: the fresh-worktree restore hint applies per worktree (`RestoreRequired` path unchanged).
- ADR-0016: the file-tool boundary is per run's workspace — child runs included; a cross-worktree write is refused like any outside-root path.
- ADR-0017: worktree management stays extension territory; extensions gain no per-workspace service API (the registry is core-internal); trust stays keyed by repository identity with the content hash per worktree.

## Consequences

- Subagent runs are no longer always in the parent's directory; every consumer of a child's working directory (approvals, session headers, tool boundaries) reads the child's own workspace.
- A subagent spawned into a worktree cannot further spawn (depth stays 1) — the sample's own tool performs the spawning.
- The Roslyn tools become product-visible for the first time; the offer gating keeps non-C# directories untouched, and the startup budget stays clean because nothing loads before the first call.
- Cap behaviour is observable for tests (eviction victim by root, pin behavior); a fully pinned cap waits rather than failing, so a pathological pin storm degrades to slowness, not errors.
- If a future need forces a new session-header field for child workspaces, that bump and its migration are recorded in a follow-up ADR — the current fields suffice.

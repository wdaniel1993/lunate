# Sample: worktree-tasks (T-51)

## Why

T-51 (guide row 639; fitness row `docs/spec/extensibility.md` 121): *"workspace per run, per-workspace services and limits, session grouping, trust by repository identity; two subagents in separate worktrees edit and build independently (Roslyn per workspace, cap holds), results merge back via git, worktrees are cleaned up."*

Recon against the code shows the pinned behavior is only half-built:

- **Per-run workspaces exist for top-level runs** (`Workspace`, session header `repo`/`worktree`, grouping, restore hint) — but **child runs cannot have their own**: `SubagentRequest` carries no workspace, and `SubagentRunner.CreateChildHarness` composes the child from the parent's live facts **including the working directory and the parent's tool instances** — while the spec already pins *"Every run — child runs included — has a workspace"* (`docs/spec/extensibility.md` § Workspaces and repository identity) and the agent-loop spec pins the child as *"same model client, offered tools as allowed, working directory, hooks, …"*. The subagents sample (T-47) could not work around this; it never ran a child in another worktree.
- **Per-workspace services are pinned but not built**: *"Per-workspace services — the Roslyn backend, LSP background services — are keyed by `WorktreeRoot`, started lazily and capped (default: two loaded Roslyn workspaces, least-recently-used unloaded)."* `RoslynBackend(worktreeRoot)` exists with an internal solution cache, but nothing keys backends by workspace root, nothing enforces the two-workspace cap, and the `cs_*` tools are **not composed into any run yet** (only tests construct them).

Per the fitness-suite rule (a sample needing a workaround means the core is missing a primitive), T-51 adds the missing primitives and the sample that proves them end to end.

## What Changes

- **Child runs get workspaces** (`Lunate.Agent`): `SubagentRequest` gains `WorkspaceRoot` (null inherits the parent's — today's behavior, bit-for-bit); a workspace child runs with its own working directory and its own tool set (built through a new `AgentHarnessOptions` factory seam supplied by Coding — Agent cannot construct Coding tools), and its child session header records the child's own repo/worktree identity while the file stays beside the parent's (spec-pinned). Identity detection is extracted into `Lunate.Agent` and reused by Coding's `Workspace`.
- **Per-workspace services + limits** (`Lunate.Coding`, `Lunate.Roslyn`): a per-workspace registry keyed by canonical `WorktreeRoot`; the Roslyn backend is created lazily per root and the loaded-workspace cap (default two, `roslyn.maxLoadedWorkspaces`, LRU) is enforced by unloading the least recently used. The five `cs_*` tools are composed into every run's tool set (all modes, child runs included) through this registry — first product wiring of the Roslyn tools; startup stays clean because nothing loads before the first `cs_*` call.
- **Sample `worktree-tasks`** (`samples/extensions/worktree-tasks/`): worktree management as extension territory (the spec assigns it there): `worktree__start` (git worktree + branch, then a subagent **in that worktree** via `WorkspaceRoot`), `worktree__merge` (merge back, conflicts as teaching errors), `worktree__clean` (remove worktree + branch), `worktree__list`. Kit tests against real temp git repos; Coding-level proofs for two concurrent worktree subagents, per-workspace Roslyn + cap, session grouping and headers; the trust proof (a new worktree of a trusted repository does not re-prompt) lands at the trust-store level.

## Not in Scope

- Configurable spawn depth (stays fixed at 1); LSP background services (the registry is shaped for them, only Roslyn lands); a `/worktree` command; conflict auto-resolution; out-of-process anything (T-52).
- No new extension-facing API: extensions keep session-scoped services; per-workspace keying is core-internal (the sample reaches its worktree facts through `ToolContext.WorkingDirectory` and the subagent seam).

## Impact

- `Lunate.Agent`: `Subagents.cs` (request), `SubagentRunner.cs` (child composition + session), `AgentHarnessOptions.cs` (tool factory seam), `GitIdentity` (new, extracted), `PublicAPI.Unshipped`.
- `Lunate.Coding`: `Workspace.cs` (reuse the extracted detection), a `WorkspaceServices` registry, `HarnessFactory` (compose `cs_*` tools), settings (`roslyn.maxLoadedWorkspaces`), `PublicAPI.Unshipped`.
- `Lunate.Roslyn`: backend construction through the registry (cap/eviction hooks); no tool contract changes.
- New `samples/extensions/worktree-tasks/` (extension + tests) in the solution; `tests/Lunate.Coding.Tests` proofs; a trust-level test; layering edges.
- Spec deltas: **agent-loop** (subagent runs: the workspace option, composition and limits) and **agent-sessions** (the child header records the child's own worktree). No ADR-format change — the header field exists in schema 2.

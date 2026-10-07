# 0017 — Extensibility architecture: build for the most complex extensions

- Status: accepted — 2026-10-07 (maintainer sign-off; change merged)
- Date: 2026-10-07
- Relates to: ADR 0003 (own loop), ADR 0012 (tool contract), ADR 0014 (C# backend interface); guide "Extensions"

## Context

Pi's extension API is the bar: transforming and blocking hooks, tool exposure modes including code mode, nested tool calls with parent ids and usage roll-up, MCP servers and model providers registered by extensions, compaction and context hooks, continuation at turn boundaries, session entries, renderers, mode-aware UI, project trust. Lunate must host extensions of that complexity — subagents, memory providers, LSP integrations, code mode, permission gates, model routers. T-07…T-11 have landed, so event, tool and session formats are still cheap versioned migrations. The architecture is fixed now, before frontends and stored sessions multiply.

## Decision

**Principles**

1. The core ships primitives; features (subagents, memory, LSP, code mode) are extensions. The core only adds what extensions cannot do safely alone.
2. Every hook has declared semantics: observe | transform | replace | block, deterministic order, defined failure policy.
3. Everything crossing the extension boundary is JSON-serializable — no delegates, no live objects; handles instead — so out-of-process extensions in other languages stay possible.
4. Extensions compile against a small, semver'd contract assembly, never against `Lunate.Agent` internals.
5. Nested work (tools calling tools, subagents, nested model calls) goes through the same approval policy, hooks, cancellation and usage accounting as top-level work.
6. In-process extensions are fully trusted. An `AssemblyLoadContext` isolates dependencies, not permissions; real isolation only via separate processes.
7. The architecture is proven by reference extensions running in CI (the fitness suite).

**Contract and loading**

- New assembly `Lunate.Extensibility.Abstractions`: semver'd, PublicAPI-tracked. Extensions reference only it (plus Microsoft.Extensions.AI abstractions).
- Shared in the default `AssemblyLoadContext`: the abstractions assembly, `Microsoft.Extensions.AI.Abstractions`, `System.Text.Json`. Everything else an extension brings is private to its collectible ALC.
- Manifest (`extension.json`): id, version, apiVersion range, entry assembly, declared tools/commands/hooks/services, settings JSON schema, requested capabilities (informational). The host refuses incompatible apiVersion ranges with a clear message.
- Lifecycle: the factory registers only — no processes, sockets or timers; long-lived resources start in `SessionStarted` (safe to run more than once) or on first use and stop in an idempotent `SessionEnding`. Reload unloads the ALC; state does not survive reload.
- Per-extension settings (validated against the schema) and secrets (namespaced; never written to session files).

**Hooks**

- The catalogue (semantics, order, failure policy, timeouts) is specified in `docs/spec/extensibility.md`; the built-in approval prompt runs after all `ToolCalling` handlers so the user approves the final arguments; continuations are capped per run (default 3).
- Handlers run in load order, then optional priority. All hooks are async with cancellation and a per-handler timeout.
- Extension context additions are tagged with their source, counted in the context meter, shown by `/context`, budgeted per extension, and persisted only through explicit session entries. Appending beats rewriting the prefix (prompt caching).

**Nested work and services**

- `ToolContext` exposes nested tool execution and a per-path file mutation queue; nested calls reuse validation, approval, cancellation and events, with parent ids and depth limits.
- Long-lived integrations use session-scoped background services (lazy start; a crash becomes a notice, never a Lunate crash), a file change bus, a service registry, and a model registry for nested model calls with usage charged to the calling run.

**Workspaces and repository identity**

- Every run — child runs included — has a workspace: `WorktreeRoot`, `RepoRoot` and `GitCommonDir`. Tools resolve paths and enforce the boundary against the run's workspace; `bash` runs in the worktree root.
- Project identity is the repository identity (`GitCommonDir`, or the remote URL when available) plus the worktree path. Sessions are grouped by repository and distinct per worktree; the session header records both.
- Project trust is keyed by repository identity — new worktrees of a trusted repository do not re-prompt — while the extension content-hash check still applies per worktree.
- Per-workspace services (the Roslyn backend, LSP background services) are keyed by `WorktreeRoot`, started lazily and capped (default: two loaded Roslyn workspaces, least-recently-used unloaded). `FileChanged` events carry the workspace id.
- The approval rule for writes outside tracked files uses the worktree's own index; AGENTS.md and settings discovery start at the worktree root.
- A fresh worktree has no restore output: the ADR-0006 restore check must answer with an actionable message ("run `dotnet restore` in the worktree path", or offer to run it), never a diagnostics avalanche.
- Worktree management — creating and removing worktrees, branch naming, merging results back, conflict handling, cleanup, a `/worktree` command — is extension territory, not core.

**Trust**

- Global and project extensions load with a trust prompt; project extensions are re-prompted when their files change (content hash). The `/extensions` view shows scope and state.

**Out-of-process (constraint now, build later)**

- Every hook payload/result and service call round-trips through `System.Text.Json` (conformance-tested). A later `Lunate.Extensibility.Remote` host will speak JSON-RPC over stdio, enabling TypeScript/Python extensions and real isolation.

## Consequences

- Event, tool and session formats gain extension metadata in `add-extension-formats` (schema 2 migration, ADR-0018) while migrations are still cheap.
- The core stays small: subagents, memory, LSP, code mode and model routing ship as reference extensions; "subagent-ready core" replaces "no subagents" as the honest framing.
- An extension that needs a workaround means the core is missing a primitive — the fitness suite makes that visible in CI.
- In-process trust means a malicious extension is as powerful as Lunate itself; the mitigation is the trust prompt, scope visibility and, later, the out-of-process host — not the ALC.

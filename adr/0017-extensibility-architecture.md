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
- Handlers run by priority descending with a stable load-order tie-break (higher priority first; ties keep load order). All hooks are async with cancellation and a per-handler timeout.
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

- Project extensions load with a one-time trust prompt per repository identity (re-prompted when their files change — content hash); a hand-placed user-scope extension is trusted by placement, while a packaged or installed one is pending in both scopes until the question is answered (ADR 0017 addendum — Packages, P7). The `/extensions` view shows scope and state.

**Out-of-process (constraint now, build later)**

- Every hook payload/result and service call round-trips through `System.Text.Json` (conformance-tested). A later `Lunate.Extensibility.Remote` host will speak JSON-RPC over stdio, enabling TypeScript/Python extensions and real isolation.

## Consequences

- Event, tool and session formats gain extension metadata in `add-extension-formats` (schema 2 migration, ADR-0018) while migrations are still cheap.
- The core stays small: subagents, memory, LSP, code mode and model routing ship as reference extensions; "subagent-ready core" replaces "no subagents" as the honest framing.
- An extension that needs a workaround means the core is missing a primitive — the fitness suite makes that visible in CI.
- In-process trust means a malicious extension is as powerful as Lunate itself; the mitigation is the trust prompt, scope visibility and, later, the out-of-process host — not the ALC.

## Amendment 2026-10-26 — the approval panel is the boundary

The decisions above say what Lunate asks and records. They do not confine a process that already has the user's shell. This amendment states the position the code and the documentation implement, so no later reader takes a record for a wall:

- **In default mode the approval panel is the boundary.** Every `bash` command, every MCP tool and every write outside tracked files is shown before it runs. A high-risk call — `lunate trust`, `lunate install`, `lunate uninstall`, or a command or write aimed at the state root — always shows, even after "always" was granted for that tool.
- **`--yolo` removes it.** The flag is per run, never a saved setting, and the run prints one line saying that the approval boundary — and therefore the trust boundary — is off.
- **The state root is not a security boundary.** `LUNATE_HOME` (default `~/.lunate`) holds `trust.json` and the install records, and a command running with the user's shell can write it. The `LUNATE_SESSION` marker is a visibility guard a command can remove (`env -u`), and `trust.json` is a record of answers, not a lock. Neither is a security boundary against the bash tool.
- **0.1.0 has no OS sandbox, on any platform including Windows.** The guard profile is scheduled for 0.2; this version claims no confinement.

What does not change: **installing is not trusting.** An installed extension stays pending in both scopes until its question is answered, and `IProjectTrustHandler` is a veto — `Allow` approves nothing and the prompt still runs.

## Addendum 2026-10-29 — the four contract gaps before stable (proposed)

- Status: **Proposed — 2026-10-29** (awaiting maintainer sign-off; change `extension-contract`, #268 step 2)
- Relates to: this ADR (contract and loading, workspaces and repository identity), ADR-0021 (per-workspace services), ADR-0006 (Roslyn placement), ADR-0012 (tool contract)
- Evidence: [`docs/eval/roslyn-surface-audit.md`](../docs/eval/roslyn-surface-audit.md)

### Context

The contract is published (`ExtensionApi.Current` is `1.11.0`,
`src/Lunate.Extensibility.Abstractions/ExtensionApi.cs:7`) but the flagship does not use it: the
audit's verdict is *no* (`docs/eval/roslyn-surface-audit.md:26-43`), and the host composes the five
`cs_*` tools directly (`src/Lunate.Coding/HarnessFactory.cs:151-160`). Four primitives are missing
before the contract can honestly be called stable: a built-in path, per-workspace services, a
host-evaluated activation rule, and a way to keep the `cs_*` names. This addendum decides those four
and the order in which they may be published. It records no implementation.

### (a) Built-ins register through the same path

- A host-supplied built-in manifest must declare `builtin: true`; a normal manifest omits it. It
  means the extension ships with the host: its assembly is already in the default
  `AssemblyLoadContext`; its lifecycle and version are the host's; it is never installed,
  content-hash trusted, upgraded or uninstalled; and it is supplied by the host composition, not
  discovered from a directory. A directory manifest claiming `builtin: true` is a discovery error,
  a host-supplied manifest without it is refused, and a discovered id equal to a built-in id is
  skipped naming both — a user cannot shadow a built-in.
- Discovery differs from installation: installation writes a store directory and asks the trust
  question (`src/Lunate.Extensibility/ExtensionLoader.Load.cs:64-96`); built-in discovery folds a
  host-supplied descriptor list into the loader before the global and project scans
  (`src/Lunate.Extensibility/ExtensionLoader.cs:109-123`). Built-ins have no install record, no
  content hash and no trust prompt; `/extensions` shows scope `builtin`.
- Loading reuses the existing activation tail (`src/Lunate.Extensibility/ExtensionLoader.Load.cs:200-236`):
  the same `ExtensionContext`, registries, manifest cross-checks and teardown. The built-in front
  door takes the supplied factory instead of `LoadFromAssemblyPath` and never constructs
  `ExtensionLoadContext` (`src/Lunate.Extensibility/ExtensionLoader.Load.cs:140`,
  `src/Lunate.Extensibility/ExtensionLoadContext.cs:22-24`). No second Roslyn, no collectible ALC
  for a built-in.
- The four-assembly sharing rule for third-party contexts is unchanged
  (`src/Lunate.Extensibility/HostOwnedAssemblies.cs:28-34`). A built-in uses the process-wide
  MSBuild bootstrap exactly once (`src/Lunate.Roslyn/MsBuildBootstrap.cs:15-28`) and loads no Roslyn
  before its first call (`src/Lunate.Coding/WorkspaceServices.cs:275-280`).
- Built-ins load first, in the composition's order, then global and project descriptors. They are
  not reloadable or unloadable in this version; session end stops their background services.
- `--no-extensions` still composes the built-in set: the C# tools are present today because they
  are core (`src/Lunate.Coding/HarnessFactory.cs:36-39`), and preserving that is part of the
  ordering rule. The flag still skips store discovery, trust, third-party loads and third-party
  extension MCP servers/renderers/widgets (`src/Lunate.Coding/InteractiveSession.cs:579`,
  `src/Lunate.Coding/PrintMode.cs:157`, `src/Lunate.Coding/ExtensionSession.cs:34-39`).

### (b) Per-workspace services

- `IExtensionContext` gains a registration-only workspace service facility. A named service is
  declared in the manifest `workspaceServices` list (`name`, optional positive
  `maxLoadedWorkspaces`, default 2) and registered with a factory; a registered-but-undeclared
  service refuses the load. Acquisition names a canonical `WorktreeRoot` and returns a lease; the
  lease pins the entry for its lifetime. Values are created lazily per root and disposed on
  eviction or shutdown.
- The semantics are ADR-0021's, generalized: LRU eviction of unpinned entries; a pinned entry is
  never evicted; when every loaded slot is pinned, a new root's acquisition waits, cancellation-aware
  (`adr/0021-per-run-workspaces.md:23-24`); the same registry and cap serve the session root and
  child-run roots (`src/Lunate.Coding/WorkspaceServices.cs:18-44`, `:116-167`, `:235-257`).
- **Workspace switch.** There is no global current workspace; a switch is an acquisition for a
  different key. The old root stays loaded until it is the LRU candidate and is reloaded from disk
  if a later call returns to it. A long-lived lease on the old root keeps it pinned and can make the
  new root wait, so handlers acquire per call.
- **Shutdown.** The session cancels in-flight work first; the registry then releases waiters,
  disposes every entry (pinned included), and later acquisitions fail with `ObjectDisposedException`.
  A call that resumes after shutdown settles as a cancelled/teaching result, not a crash.
- **The `SessionImages` failure class is the design constraint for eviction.** `Store` writes a
  blob and returns a reference that is attached to a message later
  (`src/Lunate.Agent/SessionImages.cs:96-150`, `src/Lunate.Agent/AgentHarness.Tools.cs:138`), while
  `Session.Load` sweeps blobs not referenced by entries (`src/Lunate.Agent/Session.cs:252-255`,
  `src/Lunate.Agent/SessionImages.Sweep.cs:64-99`); a concurrent open can delete the blob between
  the store and the append (the archive records the single-writer assumption,
  `openspec/changes/archive/2026-10-22-images/design.md:154`). `ResetForNewSession` deletes the
  whole image directory (`src/Lunate.Agent/SessionImages.cs:44-58`). Therefore eviction disposes
  rebuildable resources only: no sweep, no reference-count cleanup and no directory reset. Session
  data is session-scoped and is not a workspace service.
- Roslyn is the first consumer: service name `roslyn`, cap 2, acquired per `cs_*` call. The current
  `roslyn.maxLoadedWorkspaces` setting remains the host override during migration
  (`src/Lunate.Coding/SettingsStore.cs:68`, `:564`); moving it is the audit's P2 item and is not
  decided here.

### (c) Conditional tool availability

- A manifest may declare `toolActivation`. `*` is the default rule; other keys must be declared
  tools. The closed condition set is `always`, `workspace-root-has-file` (direct-child file globs,
  no path separator) and `workspace-is-git-repository`. A tool with no rule is always available;
  malformed rules and rules for undeclared tools are manifest errors.
- The **host** evaluates the rule when it composes each run's tool registry, against that run's
  workspace, and omits an inactive tool from declarations, nested execution and programmatic stubs.
  Extension code is never on the prompt-build path.
- The flagship's rule is the existing gate: `.sln`, `.slnx` or `.csproj` at the root
  (`src/Lunate.Coding/HarnessFactory.cs:127-145`). The generic evaluator replaces that hard-coded
  check only when the flagship migrates; until then the core check stays.
- A condition that changes mid-session is observed at the next composition (a child run, a reload
  that recomposes, or a new session), not by mutating the running registry. This preserves the
  existing once-per-composition behaviour and keeps the declared set and prompt prefix stable for
  the life of a run. Live re-evaluation is a separate decision.
- Why the host: the rule is in the trusted, content-hashed manifest; one evaluator serves core,
  built-in and third-party tools; the decision is deterministic and auditable; and it keeps
  `FunctionInvokingChatClient`/`AIFunctionFactory` out of the design.
- **The MCP server calls the same evaluator, not a copy.** `lunate mcp roslyn` answers for the
  client's declared MCP `roots`, and for its own working directory when the client declares none. No
  matching root means no `cs_*` tool in `tools/list`, with one line on stderr naming the rule and its
  patterns; stdout stays protocol-pure. On `notifications/roots/list_changed` the server re-evaluates
  and, when the offered set changed, sends `notifications/tools/list_changed`. A client that did not
  declare `listChanged` support keeps the snapshot from `initialize`. A tool listed before its
  solution vanished with no roots event still answers with the fix-it error: the call path explains a
  stale composition rather than pretending it is current.

### (d) `cs_*` stays stable

- **Decision: extension-declared stable aliases, not a reserved unprefixed namespace.** A manifest
  may declare `toolAliases` mapping a local name to a model-facing alias. The canonical
  `<id>__<local>` stays the identity for diagnostics, grouping and unload; the alias is what the
  model sees. The recommendation is aliases because they keep the namespace rule intact for
  everyone, make the exception visible in the trusted manifest, and reuse one global collision
  rule. A reserved `cs_` prefix would make the name depend on who shipped the extension and would
  permanently weaken the contract; the audit itself warns against that
  (`docs/eval/roslyn-surface-audit.md:114-117`).
- **Collision rules.** An alias is `[A-Za-z0-9_-]{1,64}`, contains no `__`, and is not a core tool
  name. Aliases are globally unique across built-ins, extensions and canonical names; a collision
  refuses the later load naming both owners. Built-ins load first, so a discovered extension cannot
  take `cs_*`. A collision with an MCP tool's model-facing name keeps the existing
  first-composition-wins rule (extension tools first, the MCP tool skipped with one diagnostic,
  `openspec/specs/extensions/spec.md:725-729`). An alias for an undeclared or unregistered tool is a
  manifest error. An alias is part of the published model-facing surface; changing it is a breaking
  change the host does not silently rename.
- The flagship's aliases are exactly `cs_diagnostics`, `cs_find_symbol`, `cs_find_references`,
  `cs_outline` and `cs_rename` — the names in `src/Lunate.Roslyn/CsDiagnosticsTool.cs:36` and its
  four siblings. No prompt, doc or template changes.

### (e) Structured results are part of the stable contract

- **Decision: `ToolResultView` carries `StructuredContent`, defined and populated now — not a
  reserved empty field.** It is a JSON object, JSON-serializable per principle 3, plus a `ContentKind`
  naming its schema (`lunate.diagnostics/v1`, `lunate.diff/v1`, `lunate.command-result/v1`). Tool
  declarations may state an optional output schema for it.
- **Aligned with MCP.** The names and shape mirror MCP's `structuredContent` and `outputSchema`, so the
  adapter maps in both directions: a configured MCP server's structured result arrives structured, and
  `lunate mcp roslyn` emits `structuredContent` for `cs_*`.
- **Populated now for what already has typed details:** the five `cs_*` tools, the edit and write
  diffs, and bash exit status with the command line and duration. `Details` stays internal; the
  projection is the published shape.
- **The model still sees `ResultText`, unchanged.** `StructuredContent` serves renderers, export,
  `--json` and MCP, so prompt behaviour cannot drift with a renderer feature.
- **The renderer API may stay minimal for 0.2**, but it receives `StructuredContent` from day one, so
  no text-only renderer surface is frozen. Without this, #237's stability promise would freeze the
  wrong shape: a renderer that can only parse text.

### The ordering rule

The contract is published as **stable only after** the flagship migrates onto it and the migration
shows that its behaviour, model-facing tool names and startup/memory/performance budgets are
unchanged. Until then the honest claim is the audit's: the C# tools are a core capability and the
contract is proven by the fitness suite (`docs/eval/roslyn-surface-audit.md:131-133`). The four
primitives land in an earlier change than the migration; publication is the last step, not the
first.

### What this addendum does not do

- No production code, tests, fixtures or migration.
- No `FunctionInvokingChatClient` and no `AIFunctionFactory` anywhere in the design: tools stay
  `ITool` and the loop executes them (`adr/0012-tool-contract.md:11-13`); activation is a host-side
  filter and workspace services are acquired inside handlers.
- No move of `roslyn.maxLoadedWorkspaces`; no built-in reload; no live mid-run activation; no
  remote per-workspace service lifecycle in v1.

### Consequences

- A built-in can be a real extension without a second load context or a second bootstrap, so the
  guide's claim can become mechanically true after the migration.
- Extensions gain the ADR-0021 lifecycle semantics, and `SessionImages`-class data is explicitly
  out of that lifecycle.
- The model-facing tool set is decided by trusted manifest data and the host, not by extension code
  at prompt-build time.
- The `cs_*` names survive without a first-party exception; the cost is one manifest field and
  global alias collision checks.
- Until the migration is green, the contract is additive but not stable, and the audit's honest
  statement stands.

# Extensibility architecture

Status: working spec under ADR-0017. The durable decisions live in `adr/0017-extensibility-architecture.md`; this document is the detail the cards build against. Behavioural requirements that are already testable live in the OpenSpec specs ([`agent-events`](../openspec/specs/agent-events/spec.md), [`agent-tools`](../openspec/specs/agent-tools/spec.md), [`agent-sessions`](../openspec/specs/agent-sessions/spec.md)) and are referenced by name, not duplicated.

## Principles

1. Core ships primitives; features (subagents, memory, LSP, code mode) are extensions. Core only adds what extensions cannot do safely alone.
2. Every hook has declared semantics: observe | transform | replace | block, deterministic order, defined failure policy.
3. Everything crossing the extension boundary is JSON-serializable (no delegates or live objects, handles instead), so out-of-process extensions in other languages remain possible later.
4. Extensions compile against a small, semver'd contract assembly, never against `Lunate.Agent` internals.
5. Nested work goes through the same approval policy, hooks, cancellation and usage accounting as top-level work.
6. In-process extensions are fully trusted. `AssemblyLoadContext` isolates dependencies, not permissions. Isolation only via separate processes.
7. The architecture is proven by reference extensions running in CI.

## Contract and loading

- **Assembly**: `Lunate.Extensibility.Abstractions` — semver'd, PublicAPI-tracked. Extensions reference it and `Microsoft.Extensions.AI.Abstractions`; nothing else from Lunate.
- **ALC sharing**: the abstractions assembly, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` are shared in the default `AssemblyLoadContext`; everything else an extension brings is private to its collectible ALC.
- **Manifest** `extension.json`: `id`, `version`, `apiVersion` range, `entryAssembly`, declared `tools`/`commands`/`hooks`/`services`, `settingsSchema` (JSON schema), `capabilities` (informational). Incompatible `apiVersion` → clear refusal at load.
- **Lifecycle**: the factory registers only (no processes, sockets, timers). Long-lived resources start in `SessionStarted` (safe to run more than once) or on first use and stop in an idempotent `SessionEnding`. Reload unloads the ALC; state does not survive.
- **Settings and secrets**: per-extension settings validated against `settingsSchema`; secrets are namespaced and never written to session files.
- **Discovery**: `~/.lunate/extensions/` (global) and `.lunate/extensions/` (project). Manifests are read at startup; assemblies load on first use. Project extensions run repository code and need a one-time approval per repository, re-prompted when files change (content hash). `/extensions` shows scope, state, load time.

## Hook catalogue

Handlers run in load order, then optional priority. All hooks are async with cancellation and a per-handler timeout. `ToolCalling` runs before the built-in approval prompt, so the user approves the final arguments. Continuations requested at turn boundaries are capped per run (default 3).

| Hook | When | May | Handler failure |
| --- | --- | --- | --- |
| ProjectTrust | before project extensions load (global ext only) | allow/deny | deny |
| SessionStarted / SessionEnding | session lifecycle | start/stop resources | report |
| InputReceived | raw user input | transform or consume | report, pass through |
| RunStarting | before first model call of a run | edit prompt sections, select active tools | report |
| ContextBuilding | before every model request | request-local add/transform of messages | report, skip |
| ProviderStreamEvent | each raw update | observe only (fast path, budgeted) | report |
| MessageCompleted | assistant message final | replace (same role) | report, keep original |
| ToolCalling | before approval and execution | mutate args, block with reason | block (fail-safe) |
| ToolResultReady | after execution | transform/redact (composes in order), attach data | report, keep original |
| TurnEnded / RunSettling | actionable boundaries | append entries, request one continuation | report |
| RunSettled | final | observe | report |
| Compacting | before compaction | supply summary or strategy | fall back to default |
| ModelChanged / ToolsChanged | after change | observe | report |

Rules:

- Context added by extensions is tagged with its source, counted in the context meter, shown by `/context`, has a per-extension token budget, and is persisted only through explicit session entries.
- Prefer appending prompt/tool changes over rewriting the prefix (prompt caching).
- Every hook payload and result is a JSON DTO; a conformance test round-trips them all through `System.Text.Json`.

## Tool model

The byte-level contract is owned by [`agent-tools`](../openspec/specs/agent-tools/spec.md) and is extended by the `add-extension-formats` change; the summary:

- **Exposure**: `Direct` (model + programmatic, default), `ModelOnly` (model only), `Programmatic` (scripts only, never declared), `Deferred` (discoverable, not declared upfront), `Hidden` (internal). The registry declares only what the model should see; nested/programmatic execution respects the same gate.
- **Namespaces**: name, description, instructions — grouping for discovery and prompt rendering (MCP servers namespace their tools).
- **Annotations**: `ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld` (MCP meanings and defaults). `ToolRisk` stays an explicit override; otherwise it derives from annotations.
- **Output schema**: optional `OutputSchema`; when present, `ToolResult.StructuredContent` carries the typed result next to the model-facing text.
- **Nested calls**: `ToolContext` carries `RunId` and `ExecuteToolAsync(name, args, ct, onUpdate)` — same validation, hooks, approval and cancellation as top-level calls; ids are `"<parent>/<n>"`; depth, concurrency and token limits enforced by the core. Usage from nested model calls rolls up to the owning run.
- **Concurrency**: per-tool `Parallel | Sequential`. File-mutating tools go through `IFileMutationQueue` (per-path, read-modify-write as one unit).

## Services for long-lived integrations

- **IBackgroundService**: session-scoped, lazy start; a crash becomes a notice, never a Lunate crash (LSP servers, watchers, memory DB connections).
- **IFileChangeBus**: core publishes `FileChanged` after write/edit/rename and, where detectable, after `bash`. The Roslyn backend is the first consumer; LSP extensions subscribe.
- **IServiceRegistry**: extensions provide/consume services by contract (typed via the abstractions, or string id + JSON for out-of-process later) — for example a memory store used by several extensions.
- **IModelRegistry**: nested model calls by model id with usage charged to the calling run; `RegisterProvider` (IChatClient factory); `RegisterMiddleware` (DelegatingChatClient at defined pipeline slots); virtual models that route per request.
- **MCP**: `RegisterMcpServer(name, config)` with the `mcp.json` shape plus exposure per server/tool; each server gets a namespace.
- **IUserInteraction**: notify, confirm, select, input, status, widgets. The TUI implements all; ACP maps confirm to permission requests where possible; print/json use a non-interactive policy (deny unless configured). Custom TUI components only when the mode supports them.
- **Renderers**: tool and custom-entry renderers resolvable by tool name, including tools not registered yet (MCP tools in a resumed session).

## Workspaces and repository identity

Every run — child runs included — has a workspace: `WorktreeRoot`, `RepoRoot` and `GitCommonDir`. Tools resolve paths and enforce the boundary against the run's workspace; `bash` runs in the worktree root. Project identity is the repository identity (`GitCommonDir`, or the remote URL when available) plus the worktree path: sessions are grouped by repository and distinct per worktree, and the session header records both. Project trust is keyed by repository identity — new worktrees of a trusted repository do not re-prompt — while the extension content-hash check stays per worktree. Per-workspace services — the Roslyn backend, LSP background services — are keyed by `WorktreeRoot`, started lazily and capped (default: two loaded Roslyn workspaces, least-recently-used unloaded); `FileChanged` events carry the workspace id. The approval rule for writes outside tracked files uses the worktree's own index; AGENTS.md and settings discovery start at the worktree root. A fresh worktree without restore output gets an actionable restore hint (ADR-0006: "run `dotnet restore` in the worktree path", or offer to run it), never a diagnostics avalanche. Worktree management — creating and removing worktrees, branch naming, merging results back, conflict handling, cleanup, a `/worktree` command — is extension territory, not core.

## UI by mode

| Capability | TUI | ACP | print / json |
| --- | --- | --- | --- |
| notify | yes | log/status | stderr / event |
| confirm | approval prompt | permission request where possible, else deny | deny unless configured |
| select / input | pickers, editor | client request where possible, else deny | deny unless configured |
| status / widgets | full | status only | none |
| custom renderers | yes | no | no (events only) |

## Code mode and hosted tools

- The core provides only: `Programmatic`/`Deferred` exposure, namespaces, `OutputSchema` + `StructuredContent`, `ExecuteToolAsync`, and typed stub generation from JSON schemas.
- The code-mode extension owns the script runtime. Default: a **separate process** with timeout and resource limits, because the code is model-written. In-process options (Roslyn scripting, Jint) are opt-in and documented as not a sandbox.
- Provider-hosted tools (MEAI hosted tool types, e.g. code interpreter, web search) pass through `ChatOptions`; their result content is preserved in sessions and events.

## Out-of-process extensions (design constraint now, build later)

- Every hook payload/result and service call must round-trip through `System.Text.Json`; a conformance test covers all hook DTOs.
- All hooks are async with cancellation and a per-handler timeout.
- Later card (T-52): `Lunate.Extensibility.Remote` host speaking JSON-RPC over stdio, enabling TypeScript/Python extensions and real isolation.

## Trust, performance, testing

- Project extensions: trust prompt plus a content hash; re-prompt when extension files change. Global vs project scope shown in `/extensions`.
- Startup reads manifests only; assemblies load on first use. Per-extension load time and per-hook time are recorded; warn above a threshold. Startup budget unchanged with 10 installed extensions (tested).
- `Lunate.Extensibility.Testing`: test host with `ReplayChatClient`, scripted user interaction, event and session assertions. The template project uses it.

## Fitness suite (`samples/extensions`, CI)

| Sample | Proves |
| --- | --- |
| permission-gate | ToolCalling + annotations → block/confirm |
| memory-provider | ContextBuilding injection, TurnEnded storage via a registered service; survives compaction by re-injection |
| lsp-diagnostics | background service + file change bus + ToolResultReady attaches diagnostics; tested against a fake LSP server |
| mcp-from-extension | RegisterMcpServer, namespace, exposure |
| subagents | child harness, RunId/ParentRunId, linked child session, approvals routed to the parent UI, cancel propagated, depth/concurrency/token limits enforced by core |
| code-mode | programmatic tools via ExecuteToolAsync, StructuredContent, script runtime in a child process |
| model-router | provider + virtual model + nested model call with usage roll-up |
| ui-showcase | renderer + status widget; degrades cleanly in print/json/ACP |
| worktree-tasks | workspace per run, per-workspace services and limits, session grouping, trust by repository identity; two subagents in separate worktrees edit and build independently (Roslyn per workspace, cap holds), results merge back via git, worktrees are cleaned up |

Each sample lists the core capabilities it proves. A sample that needs a workaround means the core is missing a primitive.

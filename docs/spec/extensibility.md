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

- **Assembly**: `Lunate.Extensibility.Abstractions` — semver'd, PublicAPI-tracked. Extensions reference it and `Microsoft.Extensions.AI.Abstractions`; nothing else from Lunate. The contract carries the core `Lunate.Agent` tool types (`ToolContext`, `ToolResult`, annotations, exposure, risk) transitively, so a tool handler is written against the one core tool model.
- **ALC sharing**: the contract assemblies (`Lunate.Extensibility.Abstractions` and the `Lunate.Agent` types it references), `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` are shared in the default `AssemblyLoadContext`; everything else an extension brings is private to its collectible ALC.
- **Manifest** `extension.json`: `id`, `version`, `apiVersion` range, `entryAssembly`, declared `tools`/`commands`/`hooks`/`services`, `settingsSchema` (JSON schema), `capabilities` (informational). Incompatible `apiVersion` → clear refusal at load.
- **Lifecycle**: the factory registers only (no processes, sockets, timers). Long-lived resources start in `SessionStarted` (safe to run more than once) or on first use and stop in an idempotent `SessionEnding`. Reload unloads the ALC; state does not survive.
- **Settings and secrets**: per-extension settings validated against `settingsSchema`; secrets are namespaced and never written to session files.
- **Discovery**: `~/.lunate/extensions/` (global) and `.lunate/extensions/` (project). Manifests are read at startup; assemblies load on first use. Project extensions run repository code and need a one-time approval per repository, re-prompted when files change (content hash). `/extensions` shows scope, state, load time.
- **Skill sources**: extensions register `ISkillSource` — skill directories on disk inside the extension's own folder (v1 disk-only). Contributed skills join the session skills index and `/skills` tagged `extension '<id>'`, inherit the extension's trust and disable state, and count under the extension's context budget (enforced before the global index cap). The resource-discovery entry point of the catalogue (see the `skills` capability spec and the ADR 0017 skills addendum).

## Hook catalogue

Handlers run by priority descending, with a stable load-order tie-break (higher priority first; ties keep load order). All hooks are async with cancellation and a per-handler timeout. `ToolCalling` runs before the built-in approval prompt, so the user approves the final arguments. Continuations requested at turn boundaries are capped per run (default 3).

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
| ToolResultReady | after execution | transform/redact (composes in order); replace output, attach structured content, set UI details | report, keep original |
| TurnEnded / RunSettling | actionable boundaries | append entries, request one continuation | report |
| RunSettled | final | observe | report |
| Compacting | before compaction | supply summary or strategy | fall back to default |
| ModelChanged / ToolsChanged | after change | observe | report |

Rules:

- Context added by extensions is tagged with its source, counted in the context meter, shown by `/context`, has a per-extension token budget, and is persisted only through explicit session entries.
- Prefer appending prompt/tool changes over rewriting the prefix (prompt caching).
- Every hook payload and result is a JSON DTO; a conformance test round-trips them all through `System.Text.Json`.
- The `ToolResultReady` payload carries the call's arguments as approved, so handlers act on what ran without parsing output text; the result may replace the output, attach model-facing structured content, and set UI-only details.

## Tool model

The byte-level contract is owned by [`agent-tools`](../openspec/specs/agent-tools/spec.md) and is extended by the `add-extension-formats` change; the summary:

- **Exposure**: `Direct` (model + programmatic, default), `ModelOnly` (model only), `Programmatic` (scripts only, never declared), `Deferred` (discoverable, not declared upfront), `Hidden` (internal). The registry declares only what the model should see; nested/programmatic execution respects the same gate.
- **Namespaces**: name, description, instructions — grouping for discovery and prompt rendering (MCP servers namespace their tools).
- **Annotations**: `ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld` (MCP meanings and defaults). `ToolRisk` stays an explicit override; otherwise it derives from annotations.
- **Output schema**: optional `OutputSchema`; when present, `ToolResult.StructuredContent` carries the typed result next to the model-facing text.
- **Nested calls**: `ToolContext` carries `RunId` and `ExecuteToolAsync(name, args, ct, onUpdate)` — same validation, hooks, approval and cancellation as top-level calls; ids are `"<parent>/<n>"`; depth, concurrency and token limits enforced by the core. Usage from nested model calls rolls up to the owning run.
- **Subagent runs (production path)**: `ToolContext.Subagents` carries the run's `ISubagentRunner` (null outside a run); `RunAsync(SubagentRequest(prompt, maxSteps?, allowedTools?))` runs a child `AgentHarness` composed from the parent's live facts — same client, workspace, hooks, mutation queue, file-change sink and approver, the parent's system prompt and the request's prompt — and returns `SubagentResult` (child session id when the parent has one, child run id, output text, stop reason, error flag, tokens used). The child session is created beside the parent's and linked with `AppendChildSession` before the child starts; child events are forwarded onto the parent's stream with `ParentRunId` (ids intact, consumers scope by run id); hook payloads fired in the child run carry the child's run id and the parent run id; approvals route through the parent's approver naming the asking run (`ApprovalRequest`), and "always this session" covers the whole tree; cancelling the caller cancels the child and the runner joins it. Limits are core-enforced as teaching error results: depth is fixed at 1 in this version (a depth-1 runner refuses every spawn; not configurable, so no setting key exists), concurrency is tree-wide (`subagents.concurrency`, default 4, refused immediately while held), and the token budgets are per child (`subagents.tokenBudget`, default 100 000) and per top-level tree (`subagents.treeTokenBudget`, default 500 000), checked before each model call against the live remaining budget (an exhausted run stops with `token_limit` and keeps its partial output). Settings resolve from the `subagents` section (`unlimited` is the explicit literal that disables a limit); per-spawn values may only tighten (they clamp to the resolved ceiling). The sample `subagents` extension is the reference consumer.
- **Concurrency**: per-tool `Parallel | Sequential`. File-mutating tools go through `IFileMutationQueue` (per-path, read-modify-write as one unit).
- **Extension tools**: `IExtensionContext.RegisterTool` takes an `ExtensionToolRegistration` (local name, description, hand-written parameters schema, optional output schema, annotations, exposure, explicit risk override and an `IExtensionToolHandler`). The handler receives the same `ToolContext` and returns the same `ToolResult` as core tools. The model-facing name is `<extension-id>__<name>`, sanitized to `[A-Za-z0-9_-]` and at most 64 characters; the namespace is the extension id; the manifest `tools` list must declare every registration (registered-but-undeclared refuses the load, declared-but-unregistered warns and drops). Registered tools enter the same `ToolRegistry` at composition before the MCP tools, behave identically (exposure gate, annotations, approval with MCP defaults when no hints are declared, bounded results, teaching error on a throwing handler) and leave it on unload, which fires `ToolsChanged` and cancels in-flight calls into a cancelled result. A model-facing name collision with a later-composed MCP tool resolves first-composition-wins: the MCP tool is skipped with one single-line diagnostic naming both sources.

## Services for long-lived integrations

- **IBackgroundService**: session-scoped, lazy start; a crash becomes a notice, never a Lunate crash (LSP servers, watchers, memory DB connections).
- **IFileChangeBus**: core publishes `FileChanged` after write/edit/rename; `bash` mutations are not published (see the file change bus section). The Roslyn backend is the first consumer; LSP extensions subscribe.
- **IServiceRegistry**: extensions provide/consume services by contract (typed via the abstractions); a service that remote children consume or a remote extension's service the host calls implements `IJsonService` (async-only by construction) — for example a memory store used by several extensions.
- **Model registry**: extensions register providers (`IChatClient` factories), middleware (`DelegatingChatClient` wrappers at the `BeforeRouting`/`AroundBackend` slots — deterministic order, fail-closed) and virtual models that route per request — at load time via `IExtensionContext`, ids namespaced `ext/<extension-id>/…`; manifest-declared providers instantiate lazily with the host reading the secret (hygiene, not a security boundary). Runs reach them through `ToolContext.Models`: nested model calls charge the owning run — `UsageUpdated` carries the resolved model, with budget and tree roll-up — and resolution is virtual → programmatic → declared → catalog.
- **MCP**: `RegisterMcpServer(name, config)` with the `mcp.json` shape plus exposure per server/tool; each server gets a namespace. A server may declare `workspaceIndependent: true` to remain offered to child runs in another worktree (default false — its process runs in the parent's directory).
- **IUserInteraction**: notify, confirm, select, input, status, widgets. The TUI implements all; ACP maps confirm to permission requests where possible; print/json use a non-interactive policy (deny unless configured). Custom TUI components only when the mode supports them.
- **Renderers**: tool and custom-entry renderers resolvable by tool name, including tools not registered yet (MCP tools in a resumed session).
- **Core handles**: the host registers reserved `core/…` services looked up with `TryGetCoreService<T>` — `core/workspace`, `core/file-bus`, `core/mutation-queue` and `core/process` (`IProcessRunner`: runs one child with a timeout, an output cap and a tree kill on timeout/cancel, and starts an interactive child (`StartSessionAsync`/`IProcessSession`, line-oriented, bounded, same tree kill) for the remote extension host; the environment is exactly what the caller passes, `null` inherits for `RunAsync`; extensions pass an allow-list).

## File change bus

The core publishes one `FileChanged` notification after every successful `write`/`edit` mutation; `bash` and writes made outside the tool path are not published. Delivery is ordered per workspace; a flush observes delivery without ending it, and the session-end drain is one-way (later notifications are dropped). The Roslyn backend is the bus's first core subscriber: the session's per-workspace services subscribe once (all changes, filtered in the handler) and route each event by workspace id to that root's backend — `.cs` paths become exact dirty marks, and project, solution and build-configuration files (`.csproj`, `.sln`, `.slnx`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`) mark the workspace for a reload on the next call. A mark never creates a backend: a root that has not loaded ignores the event, because its first load reads disk anyway. The detection rule is stated honestly: **tool edits are exact via the bus** — a tool edit is detected even when its content stamp is unchanged — while **bash and external edits are caught by the content-stamp scan**; a **same-stamp change made outside the tool path is seen only at the next reload**. A file flagged by both paths is refreshed once: one compile, one changed-file count.

## Workspaces and repository identity

Every run — child runs included — has a workspace: `WorktreeRoot`, `RepoRoot` and `GitCommonDir`. Tools resolve paths and enforce the boundary against the run's workspace; `bash` runs in the worktree root. A child spawn may name another worktree of the **same repository** (same git-common-dir) as its workspace: the child runs there with its own tool set and its session header records its own repo/worktree (the file stays beside the parent's; the parent's system prompt and loaded extensions are used as-is — no per-worktree rediscovery, no mid-run trust prompts — and project MCP tools are not offered to a different-root child unless their server is marked `workspaceIndependent`). Project identity is the repository identity (`GitCommonDir`, or the remote URL when available) plus the worktree path: sessions are grouped by repository and distinct per worktree, and the session header records both. Project trust is keyed by repository identity — new worktrees of a trusted repository do not re-prompt — while the extension content-hash check stays per worktree. Per-workspace services — the Roslyn backend, LSP background services — are keyed by `WorktreeRoot`, started lazily and capped (default: two loaded Roslyn workspaces, least-recently-used unloaded; an in-flight call pins its workspace, and a fully pinned cap waits rather than unloading under a live call — ADR-0021); `FileChanged` events carry the workspace id. The approval rule for writes outside tracked files uses the worktree's own index; AGENTS.md and settings discovery start at the worktree root. A fresh worktree without restore output gets an actionable restore hint (ADR-0006: "run `dotnet restore` in the worktree path", or offer to run it), never a diagnostics avalanche. Worktree management — creating and removing worktrees, branch naming, merging results back, conflict handling, cleanup, a `/worktree` command — is extension territory, not core.

## UI by mode

| Capability | TUI | ACP | print / json |
| --- | --- | --- | --- |
| notify | yes | log/status | stderr / event |
| confirm | approval prompt | permission request where possible, else deny | deny unless configured |
| select / input | pickers, editor | client request where possible, else deny | deny unless configured |
| status / widgets | full | status only | none |
| custom renderers | yes | no | no (events only) |

## Code mode and hosted tools

- The core provides only: `Programmatic`/`Deferred` exposure, namespaces, `OutputSchema` + `StructuredContent`, `ExecuteToolAsync`, and typed stub generation from JSON schemas — `ToolStubs.Describe(ITool)` derives the model-facing name and description, typed parameters (name, mapped type, required flag, description, raw default text, enum values) and whether the tool declares an output schema. Unrecognised schema constructs degrade to `any` with a note naming the construct; an absent or non-object schema yields zero parameters; nothing here fails a run.
- The run's programmatically callable tools are stamped into `ToolContext.ToolStubs` (`Direct` + `Programmatic` + `Deferred`, registration order, null outside a run). This is what `Deferred` is for: never declared to the model, discoverable through the stubs, callable from nested execution.
- The code-mode extension owns the script runtime. Default: a **separate process** with timeout and resource limits, because the code is model-written. The sample `code-mode` extension runs model-written JavaScript in a `core/process` child with a scrubbed environment allow-list, renders the stamped stubs into a JSDoc-annotated JavaScript prelude, and bridges every script-side tool call back through `ExecuteToolAsync` over a framed JSON-lines protocol — validation, hooks, approvals, cancellation and nested `"<parent>/<n>"` ids stay in the core pipeline, and the result carries `StructuredContent` (exit code, timeout flag, the script's value, output, stderr, bridged calls). Script output can never be mistaken for protocol: `console.*` and `process.stdout.write` become log frames.
- The child process is a **lifecycle boundary** (timeout, crash containment, kill-on-cancel), not a security sandbox: model-written code runs with the user's privileges, matching the in-process trust model; the tool's destructive annotation and the approval ask are the consent gate. In-process options (Roslyn scripting, Jint) are opt-in and documented as not a sandbox either.
- Provider-hosted tools (MEAI hosted tool types, e.g. code interpreter, web search) pass through `ChatOptions`; their result content is preserved in sessions and events.

## Out-of-process extensions

- Every hook payload/result and service call round-trips through `System.Text.Json`; the wire conformance suite covers the catalogue exhaustively (a new DTO without a wire entry fails by name).
- All hooks are async with cancellation and a per-handler timeout.
- `Lunate.Extensibility.Remote` (T-52) hosts an extension declaring a manifest `runtime` as a child process speaking newline-delimited JSON-RPC 2.0 over stdio. Manifest and wire reference for authors, with the generated JSON Schema and the conformance runner: [`docs/extensions-remote.md`](../extensions-remote.md). The reference extension is the `node-remote` sample in the fitness suite.
- The process boundary is runtime isolation (dependencies, language, crash containment), not a permission boundary; in-process trust and the trust prompt (now stating the extension runs with the user's privileges) are unchanged (ADR-0017).

## Trust, performance, testing

- Project extensions: trust prompt plus a content hash; re-prompt when extension files change. Each project MCP config file (`.lunate/mcp.json`, `.agents/mcp.json`, root `.mcp.json`) needs the same file-level decision, and every server it defines a one-time per-server approval keyed by repository identity and the canonical hash of its raw definition — the prompt shows the full command, and a changed definition re-asks. Global vs project scope shown in `/extensions`.
- Startup reads manifests only; assemblies load on first use. Per-extension load time and per-hook time are recorded: dispatches aggregate per (extension, hook) — calls, total, last and maximum milliseconds plus slow, timed-out and cancelled counts, thread-safely and without per-dispatch allocation, with child runs in the same session aggregate and a `System.Diagnostics.Metrics` histogram on meter `Lunate.Extensibility` — and the first crossing of the slow threshold (default 500 ms) warns once naming the extension, the hook, the elapsed time and the threshold and pointing at `/extensions` (later crossings count only; a cancelled dispatch is counted but never slow; a remote extension's lazy start and `initialize` handshake are excluded from the first dispatch and reported as its start duration). `/extensions` shows the slowest hook above the threshold (`hook <Hook> max <ms> ms, <slow>/<calls> slow`) and a remote extension's `start <ms> ms`; `--json` carries `hookTimings` (`{hook, calls, totalMs, lastMs, maxMs, slowCount, timedOut, cancelled}`) and `startMs`, never in the register-only `lunate extensions` inspect mode. Startup budget unchanged with 10 installed extensions (tested).
- `Lunate.Extensibility.Testing`: test host with `ReplayChatClient`, scripted user interaction, event and session assertions. The template project uses it.

## Fitness suite (`samples/extensions`, CI)

| Sample | Proves |
| --- | --- |
| permission-gate | ToolCalling + annotations → block/confirm |
| memory-provider | ContextBuilding injection, TurnEnded storage via a registered service; survives compaction by re-injection |
| lsp-diagnostics | background service + ToolResultReady attaches diagnostics (the call's arguments carry the path; structured content on the result); tested against a fake LSP server |
| mcp-from-extension | RegisterMcpServer, namespace, exposure |
| subagents | `ToolContext.Subagents` + one spawn tool: parent/child runs on a child harness, linked child session beside the parent's, events forwarded with `ParentRunId`, approvals routed to the parent UI with the asking run, cancel propagated and joined, depth (fixed 1) / tree-wide concurrency / per-child and per-tree token budgets enforced by core (`subagents.*` settings) |
| code-mode | typed tool stubs + the stamped callable set, programmatic tools via ExecuteToolAsync (approvals, nested ids), StructuredContent, model-written script in a `core/process` child with a scrubbed environment and a framed protocol |
| model-router | provider + virtual model + nested model call with usage roll-up |
| ui-showcase | renderer + status widget; degrades cleanly in print/json/ACP |
| worktree-tasks | workspace per run, per-workspace services and limits, session grouping, trust by repository identity; two subagents in separate worktrees edit and build independently (Roslyn per workspace, cap holds), results merge back via git, worktrees are cleaned up |
| node-remote | out-of-process host: a Node child speaks the wire (handshake, hook forwarding with reentrancy, services both ways, shutdown); conformance runner green; loaded through the real host |

Each sample lists the core capabilities it proves. A sample that needs a workaround means the core is missing a primitive.

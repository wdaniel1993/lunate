# extensions Specification

## Purpose

The extension system: a small, semver'd contract assembly that extensions compile against, manifest-driven discovery and lazy loading into collectible load contexts, per-extension settings and secrets, and one-time project trust keyed by repository identity.

## Requirements

### Requirement: Extension contract assembly
Extensions SHALL compile against `Lunate.Extensibility.Abstractions` and `Microsoft.Extensions.AI.Abstractions` only — never against `Lunate.Agent` internals. The contract assembly SHALL be semver'd with a PublicAPI-tracked surface, and its entry point SHALL be `IExtensionFactory.Create(IExtensionContext)` returning an `IExtension`. The context SHALL expose the extension id, settings, secrets and a log sink. The current API version SHALL be a single constant, and the assembly SHALL depend on no packages beyond the MEAI abstractions.

#### Scenario: An extension compiles against the contract only
- **GIVEN** an extension project referencing the abstractions and MEAI abstractions
- **WHEN** it is built
- **THEN** it compiles without any reference to `Lunate.Agent`

#### Scenario: The API version is queryable
- **GIVEN** the contract assembly
- **WHEN** the current API version is read
- **THEN** it is a single semver constant that the range matcher validates against

### Requirement: Extension manifest
An extension SHALL declare an `extension.json` manifest with `id` (lowercase, `[a-z0-9-]+`), `version`, `apiVersion` (a range), `entryAssembly` and optional `tools`, `commands`, `hooks`, `services`, `settingsSchema` and `capabilities`. Unknown top-level fields SHALL be ignored; malformed known fields SHALL fail with an error naming the file and the field; duplicate declarations SHALL be rejected. The API-version range grammar SHALL be exact `x.y.z` or caret `^x.y.z`; other forms SHALL be refused as unsupported. An incompatible range SHALL be refused with a message naming the extension, its range and the current API version.

#### Scenario: A valid manifest parses
- **GIVEN** a manifest with all required fields
- **WHEN** it is parsed
- **THEN** the parsed model carries every declared field and defaults the optional arrays to empty

#### Scenario: Unknown fields are forward-compatible
- **GIVEN** a manifest with an unknown top-level field
- **WHEN** it is parsed
- **THEN** it parses successfully and the unknown field is ignored

#### Scenario: An incompatible range is refused
- **GIVEN** a manifest whose `apiVersion` excludes the current API version
- **WHEN** it is validated
- **THEN** the refusal names the extension, its range and the current API version

### Requirement: Extension loading

The host SHALL discover extensions in `~/.lunate/extensions/` (global) and `.lunate/extensions/` (project), reading manifests only — assemblies SHALL load lazily on first use into a collectible `AssemblyLoadContext` that shares the abstractions, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` with the default context and isolates every other dependency. Unload SHALL be idempotent; a later load SHALL work; state SHALL not survive. Duplicate ids across scopes SHALL be an error naming both paths. Missing dependencies SHALL yield an actionable error. The CLI SHALL compose the host when a session starts — the host's first use — for interactive, print and ACP sessions; `--version`, `--help` and non-session commands SHALL NOT read the extension store. `--no-extensions` SHALL start every mode without composing the host (extension hooks, renderers, widgets and extension MCP servers absent; configured `mcp.json` servers unaffected). A discovery or load failure SHALL be logged through the host log and SHALL NOT abort the session.

#### Scenario: Manifests are read without loading assemblies

- **GIVEN** an installed extension
- **WHEN** discovery runs
- **THEN** its manifest is read and its assembly is not loaded

#### Scenario: A loaded extension unloads and reloads

- **GIVEN** a loaded extension
- **WHEN** it is unloaded and loaded again
- **THEN** both operations succeed and the second instance is fresh

#### Scenario: A missing dependency fails actionably

- **GIVEN** an extension whose entry assembly requires an assembly that is not present
- **WHEN** it is loaded
- **THEN** loading fails with an error naming the missing assembly and the extension

#### Scenario: A session composes the host at start

- **GIVEN** an extension installed in `~/.lunate/extensions/`
- **WHEN** an interactive, print or ACP session starts
- **THEN** the extension is discovered and loaded as the session starts, and its hook observes the session's tool calls

#### Scenario: Non-session commands never touch the store

- **GIVEN** any installed extension
- **WHEN** a non-session command (`lunate --version`, `lunate --help`, `lunate --discover`) runs
- **THEN** no manifest is read and no assembly is loaded

#### Scenario: One broken extension does not stop the session

- **GIVEN** an installed extension whose load fails
- **WHEN** a session starts
- **THEN** the failure is logged through the host log and the session runs without that extension

#### Scenario: --no-extensions starts without the host

- **GIVEN** an installed extension and a globally configured `mcp.json` server
- **WHEN** a session starts with `--no-extensions`
- **THEN** the extension store is not read, its hook never runs, and the configured MCP server still works

### Requirement: Extension settings and secrets
Per-extension settings SHALL be read from the host store and validated against a documented subset of the manifest's `settingsSchema` (top-level `required` and one-level `type` checks), with violations naming the extension and the field. Secrets SHALL be namespaced per extension, exposed read-only, and SHALL never be written to session files or logs.

#### Scenario: A settings violation is actionable
- **GIVEN** a settings value violating the schema subset
- **WHEN** the extension is loaded
- **THEN** loading fails naming the extension and the field

#### Scenario: Secrets stay out of sessions
- **GIVEN** a loaded extension whose secrets are read
- **WHEN** a session is written during that run
- **THEN** no secret value appears in the session file

### Requirement: Project extension trust

Project extensions SHALL require a one-time trust decision per repository identity (host-provided, `GitCommonDir` per the worktree architecture), re-prompted when the extension content hash changes for a worktree. New worktrees of a trusted repository SHALL not re-prompt. Denial SHALL refuse the load. Global extensions SHALL not be gated. Content hashes SHALL be computed over the extension directory's files deterministically, and trust records SHALL be written atomically. In interactive sessions the decision SHALL be asked as a live-area prompt answered with keys through the normal key path (deny by default); print and ACP sessions SHALL refuse an untrusted project extension with a logged hint pointing at the interactive path. A re-prompt caused by changed files SHALL say so. After any answer, one notice SHALL state that the decision is remembered for this repository and how to change it: `/trust` lists this repository's records and `/trust clear` clears them, so the next load asks again.

#### Scenario: An untrusted project extension prompts once

- **GIVEN** an untrusted repository with a project extension
- **WHEN** the extension is loaded and approved
- **THEN** the decision is recorded and a later load does not prompt again

#### Scenario: A content change re-prompts

- **GIVEN** a trusted project extension whose files changed
- **WHEN** it is loaded
- **THEN** the user is prompted again

#### Scenario: Denial refuses the load

- **GIVEN** an untrusted project extension
- **WHEN** the user denies the prompt
- **THEN** the load is refused

#### Scenario: Interactive sessions ask in the live area

- **GIVEN** an untrusted repository with a project extension
- **WHEN** an interactive session starts
- **THEN** the question renders in the live area and a `y` key approves it, with no stdin line read

#### Scenario: A re-prompt says the files changed

- **GIVEN** a project extension whose files changed since it was trusted
- **WHEN** an interactive session starts
- **THEN** the question says the files changed since this repository trusted it

#### Scenario: Trust decisions are revisable

- **GIVEN** a repository whose project extension was approved
- **WHEN** `/trust clear` is submitted and a new session starts
- **THEN** the question is asked again

### Requirement: Hook contract
The contract assembly SHALL define typed payload/result DTOs and handler interfaces for every hook in the catalogue (`ProjectTrust`, `SessionStarted`, `SessionEnding`, `InputReceived`, `RunStarting`, `ContextBuilding`, `ProviderStreamEvent`, `MessageCompleted`, `ToolCalling`, `ToolResultReady`, `TurnEnded`/`RunSettling`, `RunSettled`, `Compacting`, `ModelChanged`/`ToolsChanged`). Every payload and result SHALL round-trip through `System.Text.Json` (conformance-tested). Extensions SHALL register handlers through `IExtensionContext.Register` at creation time only — registration SHALL NOT start processes, sockets or timers. The API version SHALL move as a semver-minor addition.

#### Scenario: Every hook DTO round-trips through JSON
- **GIVEN** a payload or result DTO of any catalogue hook
- **WHEN** it is serialized and deserialized
- **THEN** the values are identical

#### Scenario: Registration is registration only
- **GIVEN** an extension registering handlers
- **WHEN** its factory runs
- **THEN** no processes, sockets or timers are started

### Requirement: Hook dispatch semantics
Handlers SHALL run by priority descending with a stable load-order tie-break (higher priority first; ties keep load order). Each hook's semantics class SHALL be enforced: observe handlers all run with results ignored; transform handlers chain in order (each sees the previous output, final value used); replace handlers chain and the final value is persisted; block handlers stop at the first block (final mutated arguments are what the user approves). Ordering SHALL be deterministic.

#### Scenario: Order is deterministic
- **GIVEN** handlers from several extensions with and without priorities
- **WHEN** a hook dispatches
- **THEN** handlers run by priority descending with a stable load-order tie-break, across runs

#### Scenario: Transforms chain
- **GIVEN** two transform handlers
- **WHEN** a hook dispatches
- **THEN** the second sees the first's output and the final value is used

#### Scenario: The first block wins
- **GIVEN** two blocking handlers
- **WHEN** a hook dispatches
- **THEN** the chain stops at the first block and its reason is surfaced

### Requirement: Hook failure policy
Every hook SHALL have a declared failure policy per the catalogue (report / skip / keep original / fail-safe block / deny / fall back), applied on handler exception and on timeout. A failing handler SHALL never crash the run; the failure SHALL be reported through the host log sink. `ToolCalling` failures (throw or timeout) SHALL block fail-safe; `ProjectTrust` failures SHALL deny.

#### Scenario: A throwing ToolCalling handler blocks
- **GIVEN** a ToolCalling handler that throws or times out
- **WHEN** the tool call dispatches
- **THEN** the call is blocked with a reason and the failure is reported

#### Scenario: A throwing transform handler keeps the original
- **GIVEN** a transform handler that throws
- **WHEN** the hook dispatches
- **THEN** the original value is used and the failure is reported

### Requirement: Timeouts and continuation caps
Each handler SHALL run under a per-handler timeout (configurable; `ProviderStreamEvent` on a shorter fast path). Continuations requested at turn boundaries SHALL be capped per run (default 3); requests beyond the cap SHALL be ignored and logged.

#### Scenario: A hanging handler is cut off by policy
- **GIVEN** a handler that never completes
- **WHEN** its timeout elapses
- **THEN** its hook's failure policy applies

#### Scenario: Continuation requests are capped
- **GIVEN** a run whose turn-boundary handlers keep requesting continuations
- **WHEN** the cap is reached
- **THEN** further requests are ignored and logged

### Requirement: Hook wiring

Hooks SHALL fire where their producers exist: `SessionStarted`/`SessionEnding` (loader lifecycle; started safe to run more than once, ending idempotent), `RunStarting`, `ContextBuilding`, `ProviderStreamEvent`, `MessageCompleted`, `ToolCalling` (before the approval prompt, with the resolved tool's annotations — lowercase kebab wire names in declaration order: `read-only`, `destructive`, `idempotent`, `open-world` — on the payload so policy handlers can decide on them), `ToolResultReady` (composing in order; the payload carries the call's arguments as approved, and the result may replace the output, attach model-facing structured content, and set UI-only details), `TurnEnded`, `RunSettled`, `ProjectTrust` (global-extension handlers in the trust flow), and `Compacting` (before compaction; the last provided summary wins, and handler failure falls back to the default summarization). Hooks without producers yet (`InputReceived`) SHALL be contract-complete and documented as unwired. The harness SHALL NOT depend on the extension system: it exposes no-op seam points, and the host adapter maps to the hook DTOs.

#### Scenario: ToolCalling runs before approval

- **GIVEN** a ToolCalling handler mutating arguments
- **WHEN** a tool call is approved
- **THEN** the user approves the final (mutated) arguments

#### Scenario: The ToolCalling payload carries the tool's annotations

- **GIVEN** a tool declared with annotations (for example `Destructive`)
- **WHEN** a call to it reaches the ToolCalling hook
- **THEN** the payload lists the tool's annotations, and a policy handler can block the call or let it fall through to the approval prompt on that basis

#### Scenario: A provided summary overrides the default

- **GIVEN** a Compacting handler returning `Provide` with a summary
- **WHEN** compaction runs
- **THEN** the provided summary is recorded instead of the default summarization

#### Scenario: The harness stays extension-agnostic

- **GIVEN** a harness configured without hooks
- **WHEN** it runs
- **THEN** behavior is unchanged

#### Scenario: The ToolResultReady payload carries the call's arguments

- **GIVEN** a ToolResultReady handler
- **WHEN** a tool call completes
- **THEN** the payload carries the call's arguments as approved, and a handler returning structured content sees it on the tool result

### Requirement: Extension context tagging
Context added by extensions SHALL be tagged with its source extension id and SHALL respect a per-extension token budget; over-budget additions SHALL be dropped and logged.

#### Scenario: Over-budget additions are dropped
- **GIVEN** an extension adding context beyond its budget
- **WHEN** the context is merged
- **THEN** the excess is dropped and logged with the extension id

### Requirement: Background services
Extensions SHALL register background services (`IBackgroundService` with `StartAsync`/`StopAsync`) at creation time. The host SHALL start them with `SessionStarted` semantics (safe to run more than once; started services are not restarted) and stop them idempotently on `SessionEnding`; unloading SHALL drop them. A start failure SHALL be reported with the extension id and service name, SHALL prevent that extension's remaining unstarted services from starting, and SHALL stop its already-started services best-effort; stop failures SHALL be reported and never thrown to the lifecycle caller.

#### Scenario: A service starts and stops with the session
- **GIVEN** an extension with a registered service
- **WHEN** the session starts and ends
- **THEN** the service starts once and stops once, idempotently on repeat

#### Scenario: A failing start stops the extension's remaining services
- **GIVEN** an extension whose first service throws on start
- **WHEN** the session starts
- **THEN** the failure is reported and the remaining services do not start

### Requirement: File change bus

The core SHALL emit a `FileChanged` notification (canonical path + workspace id) after every successful file mutation performed by the `write` and `edit` tools; refused or failed mutations SHALL emit nothing. Extensions SHALL subscribe with a handler interface and an optional `*`/`?` pattern filter. Delivery SHALL be ordered per workspace and SHALL never throw into the mutation path; handler failures follow the hook failure policy (report). The workspace id SHALL be the run's `WorktreeRoot`. The mutation queue integration SHALL deliver exactly one event per successful mutation. **Not yet wired in production** (tracked separately): only the test kit registers `core/file-bus` today, so production sessions emit nothing until that wiring lands.

#### Scenario: Events after write and edit

- **GIVEN** a subscribed extension
- **WHEN** `write` and `edit` mutate files successfully
- **THEN** one event per mutation arrives with the canonical path and the workspace id

#### Scenario: No event for a refused mutation

- **GIVEN** an `edit` that is refused (no match or ambiguous)
- **WHEN** it returns
- **THEN** no event is emitted

#### Scenario: Ordering under the queue

- **GIVEN** concurrent queued mutations on one file
- **WHEN** they complete
- **THEN** events arrive in the same order as the mutations applied

### Requirement: Service registry

Extensions SHALL register named services and look them up by name; duplicate names SHALL be refused with an error naming both extensions; reserved core service names SHALL not be shadowable. Core services SHALL be exposed to extensions as documented read-only handles (file bus subscription, mutation queue scheduling, workspace identity). The host SHALL register the workspace identity handle (`core/workspace`) when it composes a session — production sessions and the test kit alike — before any extension loads.

#### Scenario: Duplicate service names are refused

- **GIVEN** two extensions registering the same service name
- **WHEN** the second registers
- **THEN** registration fails naming both extensions

#### Scenario: Core services are reachable

- **GIVEN** an extension
- **WHEN** it looks up a core service by its reserved name
- **THEN** it receives the documented handle

#### Scenario: Workspace identity is reachable in a composed session

- **GIVEN** a composed session
- **WHEN** an extension looks up `core/workspace`
- **THEN** it receives the session's workspace identity (worktree root, repo root, git common dir)

### Requirement: Model provider registry
Extensions SHALL declare model providers in the manifest (`id`, `displayName`, `endpoint`, `secretName`, `modelIds`). Declarations SHALL be validated (namespaced id, absolute http(s) endpoint, non-empty model ids, `secretName` referencing the secrets store by name — never a value); duplicate provider ids SHALL be refused. The registry SHALL list declared providers; instantiating clients and routing land with the model-router card.

#### Scenario: A declared provider is listed
- **GIVEN** a manifest declaring a valid model provider
- **WHEN** the extension loads
- **THEN** the registry lists it with its display name, endpoint and model ids

#### Scenario: Secret values never appear
- **GIVEN** a provider declaration with a `secretName`
- **WHEN** the provider is listed or logged
- **THEN** only the name appears, never the secret value

#### Scenario: Duplicate provider ids are refused
- **GIVEN** two extensions declaring the same provider id
- **WHEN** the second loads
- **THEN** loading fails naming both extensions

### Requirement: Extension UI contributions

The contract assembly SHALL define a JSON-serializable text-segment model (`TextSegment` with a style of plain, dim, accent, warning or error) and the handler interfaces `IToolResultRenderer` (rendering a named tool's result into segments) and `IStatusWidget` (extra footer segments), registered through `IExtensionContext` at creation time only. Every UI DTO SHALL round-trip through `System.Text.Json`. Renderers SHALL match by tool name (ordinal); several renderers for one name SHALL resolve to the last registration with the conflict logged. A widget SHALL be invoked on each status update; a throwing widget or renderer SHALL be omitted for that update and reported through the host log, then retried on the next update — the hook failure policy applied to UI. The API version SHALL move as a semver-minor addition.

#### Scenario: UI DTOs round-trip through JSON

- **GIVEN** a text segment, a tool-result view and a status view
- **WHEN** each is serialized and deserialized
- **THEN** the values are identical

#### Scenario: Registration is registration only

- **GIVEN** an extension registering a renderer and a widget
- **WHEN** its factory runs
- **THEN** no rendering is invoked and no processes, sockets or timers are started

#### Scenario: Renderer conflicts resolve to the last registration

- **GIVEN** two renderers declaring the same tool name
- **WHEN** a result of that tool arrives
- **THEN** the last registered renderer runs and the conflict is logged

#### Scenario: A throwing contribution is omitted, reported and retried

- **GIVEN** a widget that throws on its first invocation
- **WHEN** two status updates occur
- **THEN** the first omits it and logs the failure, the run continues, and the second invokes it again

### Requirement: MCP servers from extensions

The contract assembly SHALL define a JSON-serializable MCP server registration — the `mcp.json` shape (name, command, args, env, working directory, call timeout), an exposure (default `Direct`) and per-tool exposure overrides by the server's own tool name — and `IExtensionContext` SHALL gain `RegisterMcpServer` for it. Registration SHALL perform no I/O and start nothing; the first tool listing starts the process exactly once (the existing lazy-server behaviour). The loader SHALL collect registrations at creation time and expose them; the first registration per server name SHALL win, and a duplicate SHALL be refused and logged naming both extensions. Each server SHALL give its tools a namespace named after the server, and the configured exposure and overrides SHALL gate the wrapped tools exactly as for core tools; an override naming no listed tool SHALL be logged. A server that fails to start or list SHALL produce an actionable error naming the server, logged by the host, and the session SHALL continue without its tools.

#### Scenario: Registration is registration only

- **GIVEN** an extension registering a server whose command cannot exist
- **WHEN** the extension is loaded
- **THEN** nothing is started and nothing is logged at registration; only first use reports the failure

#### Scenario: Duplicate server names are refused and named

- **GIVEN** two extensions registering a server with the same name
- **WHEN** the second registration is collected
- **THEN** it is refused and the log names both extensions, and the first registration's server remains the one that starts

#### Scenario: Tools arrive namespaced and exposure-gated

- **GIVEN** a registered server with an exposure override marking one tool `Hidden`
- **WHEN** its tools are listed and declarations are taken
- **THEN** every tool carries the server's namespace, the hidden tool is absent from the model's declarations, and the others follow their exposure

#### Scenario: An unknown override is logged, never silently ignored

- **GIVEN** a registration whose overrides name a tool the server does not list
- **WHEN** the tools are listed
- **THEN** the host log names the unknown override and the rest proceeds normally

#### Scenario: A failing server never breaks the session

- **GIVEN** a registered server that fails to start
- **WHEN** its tools are first requested
- **THEN** the host logs an actionable error naming the server and the session continues without its tools

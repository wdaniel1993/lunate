# extensions Specification

## Purpose

The extension system: a small, semver'd contract assembly that extensions compile against, manifest-driven discovery and lazy loading into collectible load contexts, per-extension settings and secrets, and one-time project trust keyed by repository identity.

## Requirements

### Requirement: Extension contract assembly

Extensions SHALL compile against `Lunate.Extensibility.Abstractions` — the contract's own surface and the `Lunate.Agent` types its public signatures expose (`ToolContext`, `ToolResult`, `ToolAnnotations`, `ToolExposure`, `ToolRisk`) — and `Microsoft.Extensions.AI.Abstractions`; never against other `Lunate.Agent` internals and never against other Lunate assemblies. The contract assembly SHALL be semver'd with a PublicAPI-tracked surface, and its entry point SHALL be `IExtensionFactory.Create(IExtensionContext)` returning an `IExtension`. The context SHALL expose the extension id, settings, secrets and a log sink. The current API version SHALL be a single constant, and the assembly SHALL depend on no packages beyond the MEAI abstractions, and no Lunate assemblies beyond the one-way `Lunate.Agent` reference that carries the tool contract.

#### Scenario: An extension compiles against the contract only

- **GIVEN** an extension project referencing the contract assembly
- **WHEN** it is built
- **THEN** it compiles using the contract and the `Lunate.Agent` types the contract exposes, and referencing nothing beyond them

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

The host SHALL discover extensions in `~/.lunate/extensions/` (global) and `.lunate/extensions/` (project), reading manifests only — assemblies SHALL load lazily on first use into a collectible `AssemblyLoadContext` that shares the contract assemblies — `Lunate.Extensibility.Abstractions` and the `Lunate.Agent` types it references — and `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` with the default context, and isolates every other dependency. Unload SHALL be idempotent; a later load SHALL work; state SHALL not survive. Duplicate ids across scopes SHALL be an error naming both paths. Missing dependencies SHALL yield an actionable error. The CLI SHALL compose the host when a session starts — the host's first use — for interactive, print and ACP sessions; `--version`, `--help` and non-session commands SHALL NOT read the extension store. `--no-extensions` SHALL start every mode without composing the host (extension hooks, renderers, widgets and extension MCP servers absent; configured `mcp.json` servers unaffected). A discovery or load failure SHALL be logged through the host log and SHALL NOT abort the session.

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

Project extensions SHALL require a one-time trust decision per repository identity (host-provided, `GitCommonDir` per the worktree architecture), re-prompted when the extension content hash changes for a worktree. New worktrees of a trusted repository SHALL not re-prompt. Denial SHALL refuse the load. Global extensions SHALL not be gated. Content hashes SHALL be computed over the extension directory's files deterministically, and trust records SHALL be written atomically. In interactive sessions the decision SHALL be asked as a live-area prompt answered with keys through the normal key path (deny by default); print and ACP sessions SHALL refuse an untrusted project extension with a logged hint naming the approval paths — an interactive session or the `lunate trust` command. A re-prompt caused by changed files SHALL say so. After any answer, one notice SHALL state that the decision is remembered for this repository and how to change it: `/trust` lists this repository's records and `/trust clear` clears them, so the next load asks again.

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

#### Scenario: A scripted run approves through the command

- **GIVEN** an untrusted repository with a project extension
- **WHEN** `lunate trust --yes` runs and a later print session starts
- **THEN** the extension loads and no refusal hint is logged

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

Hooks SHALL fire where their producers exist: `SessionStarted`/`SessionEnding` (loader lifecycle; started safe to run more than once, ending idempotent), `RunStarting`, `ContextBuilding`, `ProviderStreamEvent`, `MessageCompleted`, `ToolCalling` (before the approval prompt, with the resolved tool's annotations — lowercase kebab wire names in declaration order: `read-only`, `destructive`, `idempotent`, `open-world` — on the payload so policy handlers can decide on them), `ToolResultReady` (composing in order; the payload carries the call's arguments as approved, and the result may replace the output, attach model-facing structured content, and set UI-only details), `TurnEnded`, `RunSettled`, `ProjectTrust` (global-extension handlers in the trust flow), and `Compacting` (before compaction; the last provided summary wins, and handler failure falls back to the default summarization). Hooks without producers yet (`InputReceived`) SHALL be contract-complete and documented as unwired. The harness SHALL NOT depend on the extension system: it exposes no-op seam points, and the host adapter maps to the hook DTOs. During a child run, the harness hook payload family (`IParentRunIdPayload`: run starting, context building, provider stream events, message completion, tool calling, tool results, turn ended, run settled, compacting) SHALL carry the child's run id and the parent run id, so handlers can correlate a run tree.

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

#### Scenario: Child-run hook payloads carry both run ids

- **GIVEN** a tool call inside a child (subagent) run
- **WHEN** its ToolCalling hook fires
- **THEN** the payload carries the child's run id and the parent run id

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

The core SHALL emit exactly one `FileChanged` notification after every successful file mutation performed by the `write` and `edit` tools; refused or failed mutations SHALL emit nothing. Each notification SHALL carry the canonical absolute path, the workspace-relative path, the workspace id, the run id when the mutation ran inside a run, and the change kind (`created` or `modified`); a write whose content is identical to the existing file still emits (`modified`). The workspace id SHALL be the run's `WorktreeRoot`. Extensions SHALL subscribe with a handler interface and an optional `*`/`?` pattern filter matched against the absolute path. Delivery SHALL be ordered per workspace and SHALL never throw into the mutation path; handler failures follow the hook failure policy (report); an event SHALL be observable only after the file write has completed — a subscriber reading the file on delivery SHALL see the new content. A flush SHALL complete when every notification enqueued so far has been dispatched, without beginning the drain — notifications arriving afterwards are still delivered. Once a drain begins, further notifications SHALL be dropped and logged. Limits: file mutations made by the `bash` tool are not published; extension and MCP tools that write files publish through their `ToolContext` file-change sink themselves — the same mechanism the core tools use. No `deleted` kind exists (no tool deletes files).

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

#### Scenario: A notification carries the full path set and kind

- **GIVEN** a subscribed extension
- **WHEN** `write` creates a file and `edit` modifies it
- **THEN** the events carry the absolute path, the workspace-relative path, the worktree-root workspace id, the run id, and the kind (`created` then `modified`)

#### Scenario: An identical-content write still emits

- **GIVEN** a subscribed extension
- **WHEN** `write` writes content identical to the file's present content
- **THEN** one event with kind `modified` arrives

#### Scenario: A subscriber reads the new content

- **GIVEN** a subscriber that reads the file when an event arrives
- **WHEN** a `write` completes
- **THEN** the read observes the new content

#### Scenario: A flush observes delivery without ending it

- **GIVEN** a subscribed extension and a dispatched notification
- **WHEN** a flush completes
- **THEN** the subscriber has seen the notification and a later mutation still delivers

#### Scenario: Events after a drain begins are dropped

- **GIVEN** a drain in progress
- **WHEN** a mutation completes
- **THEN** no event is delivered and the drop is logged

### Requirement: Service registry

Extensions SHALL register named services and look them up by name; duplicate names SHALL be refused with an error naming both extensions; reserved core service names SHALL not be shadowable. Core services SHALL be exposed to extensions as documented read-only handles (file bus subscription, mutation queue scheduling, workspace identity). The host SHALL register the core handles — the workspace identity (`core/workspace`), the file change bus (`core/file-bus`) and the mutation queue (`core/mutation-queue`) — when it composes a session, production sessions and the test kit alike, before any extension loads.

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

#### Scenario: The file bus and mutation queue are live in a composed session

- **GIVEN** a composed session and an extension subscribed through `core/file-bus`
- **WHEN** a `write` mutates a matching file
- **THEN** the subscription receives the event, and `core/mutation-queue` schedules a read-modify-write that serializes with the core's own writes

### Requirement: Model provider registry

Extensions SHALL declare model providers in the manifest (`id`, `displayName`, `endpoint`, `secretName`, `modelIds`, `contextWindow` — a positive integer — and optional `supportsTools`, default true). Declarations SHALL be validated (namespaced id, absolute http(s) endpoint, non-empty model ids, a present `contextWindow` — local models are often small, so a wrong window would mis-time compaction — `secretName` referencing the secrets store by name, never a value); duplicate provider ids SHALL be refused. The registry SHALL list declared providers. Declared providers SHALL be instantiated lazily into OpenAI-compatible clients on first use: the host reads the secret value from the extension's secrets store and the value SHALL only ever reach the client's request headers — this is hygiene for declared providers, not a security boundary (in-process extensions are fully trusted, ADR-0017). A missing secret SHALL be a teaching error naming the provider and `secretName`; a provider error reporting that tools are unsupported SHALL surface as a teaching error naming the provider and its `supportsTools` declaration.

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

#### Scenario: A declared provider is instantiated on first use
- **GIVEN** a declared provider whose `secretName` resolves in the extension's secrets store
- **WHEN** one of its model ids is first used
- **THEN** the request reaches the declared endpoint with the secret as its authorization header, and no extension-visible surface carries the value

#### Scenario: A declaration without a context window is refused
- **GIVEN** a manifest provider entry without `contextWindow`
- **WHEN** the extension loads
- **THEN** loading fails naming the field

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

The contract assembly SHALL define a JSON-serializable MCP server registration — the `mcp.json` shape (name, command, args, env, working directory, call timeout), an exposure (default `Direct`) and per-tool exposure overrides by the server's own tool name — and `IExtensionContext` SHALL gain `RegisterMcpServer` for it. Registration SHALL perform no I/O and start nothing; the first tool listing starts the process exactly once (the existing lazy-server behaviour). The loader SHALL collect registrations at creation time and expose them; the first registration per server name SHALL win, and a duplicate SHALL be refused and logged naming both extensions. A registration whose server name matches a configured `mcp.json` server SHALL be skipped before listing in every mode — the configured server wins — and the collision SHALL be logged naming both sources and surfaced by `/mcp`. Each server SHALL give its tools a namespace named after the server, and the configured exposure and overrides SHALL gate the wrapped tools exactly as for core tools; an override naming no listed tool SHALL be logged. A server that fails to start or list SHALL produce an actionable error naming the server, logged by the host, and the session SHALL continue without its tools. A started server whose process exits SHALL flip to a failed status with the reason `the server process exited` when next observed — a tool call or `/mcp` — and its tools SHALL read as unavailable. The observation points exist where the session surfaces MCP status (interactive mode); print and ACP sessions keep the existing call-error path.

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

#### Scenario: A configured server wins a name collision

- **GIVEN** a configured server and an extension registration with the same name
- **WHEN** the session composes
- **THEN** the configured server starts and lists its tools, the registration is skipped before listing, and the collision is logged naming both sources and surfaced by `/mcp`

#### Scenario: A crashed server flips to failed

- **GIVEN** a started server whose process has exited
- **WHEN** the server is next observed (a failed tool call or `/mcp` in interactive mode)
- **THEN** its status reads failed with `the server process exited` and its tools read as unavailable

### Requirement: The `lunate trust` command

The CLI SHALL provide a `lunate trust` command for scripted and CI runs, operating on the current working directory's repository identity and worktree. With no flags it SHALL print this repository's trust records, every pending decision — a project extension whose hash is unknown or changed, or an untrusted project `mcp.json` — using the same content the interactive trust question shows (extension id, version, directory, and the changed-files note when re-prompting; the config path with each server's name and command), and one machine-independent digest over all pending decisions; the digest SHALL cover pending content only — no repository identity, worktree path, extension directory or config path — so a digest pinned on any machine matches the same pending content anywhere; it SHALL record nothing. `--yes` SHALL print the same decisions and then record them in the store's sections (extension trust and the project `mcp.json`), including a new worktree of a trusted repository, which a session would record without asking; a second invocation SHALL find nothing pending and succeed. `--yes --digest <sha>` SHALL recompute the digest and record only on an exact match; on mismatch it SHALL record nothing and show the difference. A pinned digest SHALL require a pending set to verify: with nothing pending it SHALL record nothing and exit 4, while unpinned `--yes` with nothing pending SHALL still succeed. `--json` SHALL be the inspect output contract: one JSON document carrying the records, the pending decisions and the digest, canonical newlines and invariant culture; combining it with `--yes` or `--clear` SHALL be a usage error. `--clear` SHALL clear the repository's records and print what it removed. The command SHALL perform discovery and hashing only — it SHALL NOT load extension assemblies, dispatch hooks, or start MCP servers — and the hashes it records SHALL be the hashes it prints. Exit codes SHALL be 0 for success (inspect with nothing pending), 3 when inspect finds pending decisions, 4 on a digest mismatch, 2 for usage errors (unknown or conflicting flags), and 1 for unexpected failures.

#### Scenario: Inspect prints the pending decisions and records nothing

- **GIVEN** an untrusted repository with a project extension and a project `mcp.json`
- **WHEN** `lunate trust` runs
- **THEN** it prints the records and both pending decisions with the interactive question's content, and the trust file is unchanged

#### Scenario: Approval records without loading extension code

- **GIVEN** a project extension whose factory would write a marker file
- **WHEN** `lunate trust --yes` runs
- **THEN** the trust records exist and the marker is absent

#### Scenario: Approval is idempotent

- **GIVEN** a repository trusted through the command
- **WHEN** `lunate trust --yes` runs again
- **THEN** nothing new is recorded and the exit code is 0

#### Scenario: Clearing through the command re-asks

- **GIVEN** a repository whose project extension was approved
- **WHEN** `lunate trust --clear` runs and a new session starts
- **THEN** the question is asked again and the command printed what it removed

#### Scenario: Bad usage records nothing

- **GIVEN** conflicting flags such as `--yes --clear`
- **WHEN** the command runs
- **THEN** usage is printed on stderr, the exit code is 2, and nothing is recorded

#### Scenario: Inspect reports pending decisions through its exit code

- **GIVEN** an untrusted repository with a project extension
- **WHEN** `lunate trust` runs
- **THEN** the exit code is 3, and it is 0 when nothing is pending

#### Scenario: A pinned digest gates the record

- **GIVEN** an untrusted repository whose pending decisions were inspected
- **WHEN** `lunate trust --yes --digest <the printed digest>` runs
- **THEN** the records are written

#### Scenario: A mismatched digest records nothing

- **GIVEN** an untrusted repository and a digest that no longer matches the pending decisions
- **WHEN** `lunate trust --yes --digest <stale digest>` runs
- **THEN** nothing is recorded, the exit code is 4, and the pinned digest, the computed digest and the current pending decisions are shown

#### Scenario: A pinned digest with nothing pending cannot be verified

- **GIVEN** a repository with no pending decisions
- **WHEN** `lunate trust --yes --digest <sha>` runs
- **THEN** nothing is recorded, the pinned digest is shown as unverifiable, and the exit code is 4

### Requirement: Trust command security posture

The `lunate trust` command SHALL be the only non-interactive way to record trust; no environment variable, configuration key or flag SHALL make a session record trust implicitly, and no console prompt SHALL return. The command SHALL print exactly what it records before recording it. For untrusted inputs the pinned form SHALL be the CI guidance: inspect prints a digest over the pending decisions, and `--yes --digest <sha>` records only when the recomputed digest matches — a mismatch records nothing, shows the difference, and exits non-zero. Plain `--yes` SHALL be documented as being for checkouts the operator controls, never for untrusted pull requests or forks. The project `mcp.json` trust hash SHALL be computed over each server's raw definition as written — before any `${VAR}` expansion — canonically serialized, so it is machine-independent and no expanded value or secret can influence it. Records SHALL be documented as per user and machine: ephemeral CI runners SHALL run the trust step in every job.

#### Scenario: Expanded values never enter the hash

- **GIVEN** a project `mcp.json` whose definitions reference environment variables
- **WHEN** its trust is recorded with any environment values
- **THEN** the recorded hash depends only on the raw definitions and no secret value appears in the trust file

### Requirement: Extension tools

Extensions SHALL register tools through `IExtensionContext.RegisterTool` at creation time only — registration SHALL NOT start processes, sockets or timers — with a registration carrying a local name, a description, a hand-written JSON `ParametersSchema`, optional `OutputSchema`, optional `ToolAnnotations`, a `ToolExposure` (Direct default), an optional explicit `ToolRisk` override (otherwise derived from the annotations as for core tools) and an `IExtensionToolHandler`. The handler SHALL receive the same `ToolContext` and return the same `ToolResult` as core tools — one tool model, including `ctx.FileChanges`, `ctx.RunId`, `ctx.WorkingDirectory` and nested `ExecuteToolAsync` with `"<parent>/<n>"` ids and the core's depth limits. The model-facing name SHALL be `<extension-id>__<local-name>`, sanitized to `[A-Za-z0-9_-]` and at most 64 characters (no `:`); core tool names stay reserved; the namespace (discovery grouping) SHALL be the extension id. Names SHALL be unique: duplicates within the extension, across extensions, or against a core name SHALL be refused naming both sources. A composition-time collision between an extension tool and an MCP tool's model-facing name SHALL resolve first-composition-wins: extension tools compose before MCP tools, and the later tool SHALL be skipped with one single-line diagnostic naming both sources; malformed declarations (blank or invalid name, non-object schema, missing handler) SHALL be refused naming the extension and the field; registrations per extension SHALL be bounded at 32. The manifest `tools` list SHALL be enforced asymmetrically: a registered tool that the manifest does not declare SHALL refuse the load (the manifest is what the user trusted before any code loaded), while a declared tool that is not registered SHALL be logged as a warning and dropped from the offered set. Registered tools SHALL enter the same `ToolRegistry` as core tools and behave identically: the exposure gate governs declaration and nested/programmatic visibility, annotations reach the `ToolCalling` payload, the approval policy applies with MCP defaults when no hints are declared (not read-only, possibly destructive — the call asks), results are bounded, and a throwing handler becomes an error result with instructing text, never a crash; cancellation SHALL be cooperative and linked to the extension's lifetime (a handler that ignores its token runs to completion). Unloading an extension SHALL remove its tools from the registry, fire `ToolsChanged` and cancel its in-flight calls; a resumed session containing calls to an absent extension tool SHALL still render. The API version SHALL move as a semver-minor addition.

#### Scenario: A registered tool is declared and runs

- **GIVEN** an extension registering a tool with a hand-written schema
- **WHEN** a session composes and the model calls the tool
- **THEN** the declaration carries the prefixed name (`<extension-id>__<name>`), the description and the authored schema, and the handler executes with the model's arguments and the real `ToolContext`

#### Scenario: Annotations reach the ToolCalling hook

- **GIVEN** an extension tool declared `Destructive`
- **WHEN** a call to it dispatches
- **THEN** the `ToolCalling` payload lists the annotations and a policy handler can block the call on that basis

#### Scenario: Missing hints ask

- **GIVEN** an extension tool registered without annotations
- **WHEN** a call to it dispatches
- **THEN** its derived risk is not read-only, and the approval policy asks

#### Scenario: Exposure gates are respected

- **GIVEN** extension tools registered `Hidden` and `Programmatic`
- **WHEN** the model's declarations are computed
- **THEN** neither is declared, and only programmatic or nested execution can reach them

#### Scenario: Duplicates and malformed declarations are refused

- **GIVEN** an extension registering a duplicate name or a malformed declaration
- **WHEN** it registers
- **THEN** the registration is refused with an error naming the sources, the extension and the field

#### Scenario: The manifest mismatch rule is asymmetric

- **GIVEN** an extension whose manifest does not declare a tool it registers
- **WHEN** it loads
- **THEN** the load is refused; and a declared but unregistered tool name is logged as a warning and dropped from the offered set

#### Scenario: A colliding MCP tool is skipped with a diagnostic

- **GIVEN** an extension tool and an MCP tool whose model-facing names collide
- **WHEN** the session composes tools
- **THEN** the extension tool is registered, the MCP tool is skipped with one single-line diagnostic naming both sources, and the session continues

#### Scenario: The context publishes file changes and runs nested calls

- **GIVEN** an extension tool whose handler publishes a file change and makes a nested call
- **WHEN** it executes
- **THEN** the publish lands on the bus with the full payload (absolute and relative path, workspace id, run id, kind) and the nested call runs through the same pipeline with a `"<parent>/<n>"` id

#### Scenario: A handler failure teaches, unload cancels

- **GIVEN** a handler that throws, and a second call in flight when the extension unloads
- **WHEN** they settle
- **THEN** the throw becomes an error result with instructing text (never a crash), and the in-flight call is cancelled and settles as a cancelled result while the extension's tools leave the registry with `ToolsChanged` fired

#### Scenario: A resumed session renders a missing tool

- **GIVEN** a resumed session containing a call to an extension tool that is not present
- **WHEN** the call is rendered
- **THEN** it renders through the default view without a registered renderer and without a crash

### Requirement: Model registry: middleware and virtual models

Extensions SHALL register model providers, middleware and virtual models at load time (`IExtensionContext.RegisterModelProvider`, `RegisterModelMiddleware`, `RegisterVirtualModel` — registration only, no processes, sockets or timers), all ids namespaced `ext/<extension-id>/<id>`; duplicates SHALL be refused naming both extensions, and per-extension caps SHALL apply (8 providers, 8 virtual models, 16 middlewares). Middleware SHALL be `DelegatingChatClient` wrappers at defined slots (`BeforeRouting` — sees every call, including calls to virtual models; `AroundBackend` — sees only calls that reach a backend), composed deterministically: extension load order, then middleware priority, then registration order; a middleware exception SHALL fail the request closed with an error naming the owning extension, never a silent skip, and `--no-extensions` SHALL bypass all extension models and middleware. A virtual model SHALL route **per request**: each call evaluates its selector over the request's messages and resolves to one of its backends, and one client instance MAY route consecutive calls to different backends. Backends MAY themselves be virtual models; the chain SHALL be checked at first resolve for cycles and a bounded depth, and an unknown backend SHALL be a teaching error naming the virtual model and the backend. Virtual-model capabilities SHALL default to the weakest backend (minimum context window; tools only when every backend supports them); a declaration may only narrow, and an over-wide declaration SHALL be refused at first resolve. Model id resolution SHALL follow: virtual model → programmatic provider → declared provider → session catalog → teaching error listing known ids; extension-registered ids SHALL never shadow catalog model ids.

#### Scenario: Middleware wraps at its slot
- **GIVEN** a middleware registered at `AroundBackend` and one at `BeforeRouting`
- **WHEN** a call reaches a backend and another goes to a virtual model
- **THEN** the backend wrapper saw only the backend call, and the routing wrapper saw both

#### Scenario: Middleware order is deterministic
- **GIVEN** middlewares registered across two extensions and within one, with priorities
- **WHEN** a request is composed
- **THEN** wrappers apply in load order, then priority, then registration order

#### Scenario: A throwing middleware fails closed
- **GIVEN** a middleware that throws
- **WHEN** a request goes through it
- **THEN** the request fails with an error naming the owning extension and nothing reaches the provider

#### Scenario: A virtual model routes per request
- **GIVEN** a virtual model with two backends and a request-size selector
- **WHEN** two requests of different sizes are sent through one client instance
- **THEN** each request reaches its selected backend

#### Scenario: Unknown backends, cycles and unknown ids are teaching errors
- **GIVEN** a virtual model whose backend id is not registered, a virtual-model cycle, and an unknown model id
- **WHEN** each is first resolved
- **THEN** each is refused with a teaching error naming the id and the known candidates or the chain

### Requirement: Nested model calls charge the owning run

A run's tool context SHALL expose run-bound model access (`ToolContext.Models`, stamped by the harness like the other per-run seams; null outside a run). Every nested model call through the returned client SHALL emit `UsageUpdated` for the owning run — carrying the resolved model id — count into the run's token budget, and feed the inherited tree accounting when the run is a child run: the same accounting path as the run's own model calls. Inside a subagent, a tool's nested call SHALL charge the child run and roll up to the tree.

#### Scenario: Nested usage rolls up to the owning run
- **GIVEN** a tool that makes a model call through `ToolContext.Models`
- **WHEN** the provider reports usage for that call
- **THEN** the owning run's event stream carries `UsageUpdated` with those tokens and the resolved model id, and the run's token budget counts them

#### Scenario: A subagent tool's nested call charges the child
- **GIVEN** a tool running inside a subagent child run
- **WHEN** it makes a nested model call
- **THEN** the child run's usage carries the tokens and the tree accounting rolls them up to the parent

#### Scenario: Outside a run there is no model access
- **GIVEN** a `ToolContext` built outside a run
- **WHEN** `Models` is read
- **THEN** it is null

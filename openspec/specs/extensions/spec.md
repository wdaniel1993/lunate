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
An extension SHALL declare an `extension.json` manifest with `id` (lowercase, `[a-z0-9-]+`), `version`, `apiVersion` (a range), exactly one of `entryAssembly` (an in-process extension) or `runtime` (an out-of-process extension) and optional `tools`, `commands`, `hooks`, `services`, `settingsSchema` and `capabilities`. `runtime` SHALL mirror the MCP server registration shape — `command`, `args`, `workingDirectory`, `env` (values to set), `envPassthrough` (names inherited from the parent), `callTimeout` — and a manifest with both `entryAssembly` and `runtime`, or with neither, SHALL be refused naming the file and the conflict. Unknown top-level fields SHALL be ignored; malformed known fields SHALL fail with an error naming the file and the field; duplicate declarations SHALL be rejected. The API-version range grammar SHALL be exact `x.y.z` or caret `^x.y.z`; other forms SHALL be refused as unsupported. An incompatible range SHALL be refused with a message naming the extension, its range and the current API version.

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

#### Scenario: A remote manifest parses

- **GIVEN** a manifest with `runtime` instead of `entryAssembly`
- **WHEN** it is parsed
- **THEN** the parsed model carries the runtime command, args, working directory, env, envPassthrough and call timeout

#### Scenario: A manifest must declare exactly one runtime

- **GIVEN** a manifest with both `entryAssembly` and `runtime`
- **WHEN** it is validated
- **THEN** the refusal names the file and the conflict

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
Each handler SHALL run under a per-handler timeout (configurable; `ProviderStreamEvent` on a shorter fast path). Every dispatch SHALL be timed with a monotonic clock and aggregated per (extension, hook) for the session — call count, total, last and maximum milliseconds, and separate completed, timed-out and cancelled counters — thread-safely and without per-dispatch allocation, with child runs counted into the same session aggregate, and the elapsed time SHALL also be recorded to a `System.Diagnostics.Metrics` histogram on meter `Lunate.Extensibility` with `extension` and `hook` tags. A cancelled dispatch (user cancellation, run teardown) SHALL be counted as cancelled and SHALL NOT feed the maximum, the slow count or the warning — its elapsed time reflects the user; a timeout counts as the hook's cost. The first dispatch above the slow threshold (configurable, default 500 ms) SHALL log one warning naming the extension, the hook, the elapsed time and the threshold and pointing at `/extensions` — a dim notice in the interactive UI, log-only in headless and protocol modes, never stdout — and later crossings SHALL only increment the slow count. A remote extension's interceptor hook SHALL be timed at the same point, so its recorded time includes the child-process round trip; a remote observer hook goes out as a notification with no round trip, so its recorded time is the host-side send time, labeled as such, and the lazy start and `initialize` handshake SHALL be excluded from the first dispatch's timing, recorded separately as the extension's start duration. Continuations requested at turn boundaries SHALL be capped per run (default 3); requests beyond the cap SHALL be ignored and logged.

#### Scenario: A hanging handler is cut off by policy
- **GIVEN** a handler that never completes
- **WHEN** its timeout elapses
- **THEN** its hook's failure policy applies

#### Scenario: Continuation requests are capped
- **GIVEN** a run whose turn-boundary handlers keep requesting continuations
- **WHEN** the cap is reached
- **THEN** further requests are ignored and logged

#### Scenario: A slow hook warns once

- **GIVEN** a handler whose dispatches exceed the slow threshold repeatedly
- **WHEN** the hooks dispatch
- **THEN** one warning names the extension, the hook, the elapsed time and the threshold and points at `/extensions`, and the aggregate still counts every dispatch with its slow count

#### Scenario: Timings aggregate per extension and hook

- **GIVEN** two extensions handling the same hook and one of them also handling another
- **WHEN** the hooks dispatch
- **THEN** each (extension, hook) pair carries its own call count and maximum

#### Scenario: A cancelled dispatch is counted but never slow

- **GIVEN** a handler call cancelled by the user whose elapsed time exceeds the threshold
- **WHEN** the dispatch settles
- **THEN** the entry counts it as cancelled, the maximum and the slow count do not move, and no warning is logged

#### Scenario: Child runs land in the session aggregate

- **GIVEN** a subagent run dispatching a hook handled by an extension
- **WHEN** the run settles
- **THEN** the extension's hook entry counts the child run's dispatch

#### Scenario: The histogram records with extension and hook tags

- **GIVEN** a meter listener on `Lunate.Extensibility`
- **WHEN** a hook dispatches
- **THEN** a measurement arrives tagged with the extension id and the hook

#### Scenario: A cold remote start is not the first hook's time

- **GIVEN** a remote extension whose child starts on its first use
- **WHEN** its first interceptor dispatch is timed
- **THEN** the start and handshake are excluded from the hook time and reported as the extension's start duration

#### Scenario: A remote observer's time is the send time

- **GIVEN** a remote extension subscribed to an observer hook
- **WHEN** the hook dispatches
- **THEN** its recorded time is the host-side send time, with no round trip implied

The CLI SHALL provide an interactive `/extensions` command and a `lunate extensions [--json]` subcommand listing every discovered extension — failed first, then trust-denied, then loaded, each group by id — with its id, version, scope (`global`/`project`) and state (`loaded`, `trust-denied` or `failed`). Loaded extensions SHALL show their load time in milliseconds (a load above 500 ms marked `(slow)`) and their **registered** tool, command, hook and service counts, a field differing from the manifest's declared list carrying `(N declared)` — and, with at least one dispatch above the slow threshold, its slowest such hook as `hook <Hook> max <ms> ms, <slowCount>/<calls> slow`, and a remote extension its most recent start as `start <ms> ms`; trust-denied and failed extensions SHALL show the manifest's **declared** counts, and a failed extension's message SHALL be truncated to its first sentence followed by ` — see the log` when truncated. The interactive form SHALL render one segment per extension in the single-line notice discipline (segments joined `;`, prefixed `extensions: `); the subcommand SHALL print one line per extension with load times labelled `(CLI-measured)`, or with `--json` a machine-readable array of the same fields (`loadMs`, `slow` where applicable, `startMs` on a remote extension, and — when the session dispatched hooks — `hookTimings`, one `{hook, calls, totalMs, lastMs, maxMs, slowCount, completed, timedOut, cancelled}` per timed hook), and SHALL compose the host in register-only inspect mode — loading and registering trusted extensions but starting no session events, background services or MCP servers, unloading each extension and stopping the host before exit — and SHALL never prompt, so an extension it cannot trust reads `trust-denied`. It SHALL exit non-zero when any extension failed. With no extensions discovered, the interactive form SHALL read `extensions: none configured` and `--json` SHALL print `[]`; with extensions disabled (`--no-extensions`) the interactive form SHALL read `extensions disabled (--no-extensions)`. The view SHALL never print settings values, secrets or environment values, and numbers SHALL be formatted culture-invariantly.

#### Scenario: A session shows each extension's scope, state, load time and contributions

- **GIVEN** a session with one loaded global extension and one project extension whose trust was denied
- **WHEN** `/extensions` is submitted
- **THEN** one segment per extension appears ordered failed, trust-denied, loaded — the loaded one showing `(global) loaded in N ms` and its registered counts, the denied one showing `(project) trust-denied` and its declared counts

#### Scenario: The subcommand is register-only and never prompts

- **GIVEN** a store with one trusted global extension whose background service and session hooks write markers, and one untrusted project extension
- **WHEN** `lunate extensions` runs non-interactively
- **THEN** the factory ran (registration marker present) but no service-start or session-hook marker exists, nothing prompted, no trust was recorded, the host stopped, and the exit code is 0; with `--json` the same data prints as an array of objects

#### Scenario: A failed extension makes the subcommand exit non-zero

- **GIVEN** a store with an extension whose load fails
- **WHEN** `lunate extensions` runs
- **THEN** its line reads `failed` with the first sentence of the message and ` — see the log` when truncated, and the exit code is non-zero

#### Scenario: The empty and disabled views

- **GIVEN** no extensions are discovered (or the run carries `--no-extensions`)
- **WHEN** `/extensions` is submitted (or `lunate extensions` runs)
- **THEN** the interactive form reads `extensions: none configured` (`extensions disabled (--no-extensions)` when disabled) and `--json` prints `[]`

#### Scenario: A session shows the slowest hook above the threshold

- **GIVEN** a session whose ToolCalling handler exceeded the slow threshold
- **WHEN** `/extensions` is submitted
- **THEN** the loaded extension's segment shows `hook ToolCalling max <ms> ms, <slow>/<calls> slow` and `--json` carries its `hookTimings` entry with the outcome counters

### Requirement: Hook wiring

Hooks SHALL fire where their producers exist: `SessionStarted`/`SessionEnding` (loader lifecycle; session-started fires once per session, after every extension discovered at session start has loaded or failed to load, so every registered handler observes it with the full extension set — an extension loaded later does not observe it; started safe to run more than once, ending idempotent), `RunStarting`, `ContextBuilding`, `ProviderStreamEvent`, `MessageCompleted`, `ToolCalling` (before the approval prompt, with the resolved tool's annotations — lowercase kebab wire names in declaration order: `read-only`, `destructive`, `idempotent`, `open-world` — on the payload so policy handlers can decide on them), `ToolResultReady` (composing in order; the payload carries the call's arguments as approved, and the result may replace the output, attach model-facing structured content, and set UI-only details), `TurnEnded`, `RunSettled`, `InputReceived` (the input pipeline: a transformed text proceeds, a consumed input starts and queues nothing and is reported as a notice — before anything starts or queues), `ToolsChanged` (tool-registry composition changes on extension load/unload; empty run id — outside a run), `ModelChanged` (the session model switch via `/model` or the picker; empty run id — outside a run), `ProjectTrust` (global-extension handlers in the trust flow), and `Compacting` (before compaction; the last provided summary wins, and handler failure falls back to the default summarization). Every catalogue hook SHALL have a producer. The harness SHALL NOT depend on the extension system: it exposes no-op seam points, and the host adapter maps to the hook DTOs. During a child run, the harness hook payload family (`IParentRunIdPayload`: run starting, context building, provider stream events, message completion, tool calling, tool results, turn ended, run settled, compacting) SHALL carry the child's run id and the parent run id, so handlers can correlate a run tree.

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

#### Scenario: ModelChanged fires on the session model switch

- **GIVEN** a session with a registered ModelChanged handler
- **WHEN** `/model` switches to a catalog model (or the picker confirms one)
- **THEN** the handler observes the new model id with an empty run id

#### Scenario: Every registered handler observes SessionStarted

- **GIVEN** a session with two extensions that both register SessionStarted handlers, the one loaded first sorting before the other
- **WHEN** the session starts
- **THEN** both handlers run, and the later-loaded extension's handler observes the full extension set (a cross-extension service lookup resolves)

#### Scenario: An extension loaded later does not observe SessionStarted

- **GIVEN** a running session whose SessionStarted has already fired
- **WHEN** another extension is loaded at runtime
- **THEN** its SessionStarted handler does not run

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

The core SHALL emit exactly one `FileChanged` notification after every successful file mutation performed by the `write` and `edit` tools; refused or failed mutations SHALL emit nothing. Each notification SHALL carry the canonical absolute path, the workspace-relative path, the workspace id, the run id when the mutation ran inside a run, and the change kind (`created` or `modified`), and the source (`tool` or `bash`); a write whose content is identical to the existing file still emits (`modified`). The workspace id SHALL be the run's `WorktreeRoot`. Extensions SHALL subscribe with a handler interface and an optional `*`/`?` pattern filter matched against the absolute path. Delivery SHALL be ordered per workspace and SHALL never throw into the mutation path; handler failures follow the hook failure policy (report); an event SHALL be observable only after the file write has completed — a subscriber reading the file on delivery SHALL see the new content. A flush SHALL complete when every notification enqueued so far has been dispatched, without beginning the drain — notifications arriving afterwards are still delivered. Once a drain begins, further notifications SHALL be dropped and logged. Limits: after each bash call the host SHALL diff the workspace's `git status` against the previous scan of the same run and publish every path that really changed through the same sink — git worktrees only; untracked files included; untracked directories, `.gitignore`d files and submodule dirty state excluded; an already-dirty file is not re-published without a real change, a revert is published and a commit is not; a scan that cannot complete (timeout, no repository) leaves the content-stamp scan as the backstop. Extension and MCP tools that write files publish through their `ToolContext` file-change sink themselves — the same mechanism the core tools use. No `deleted` kind exists (no tool deletes files).

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

#### Scenario: A bash call publishes only real changes

- **GIVEN** a git worktree with an already-dirty file and a run whose first bash call has baselined
- **WHEN** a bash call changes one other file
- **THEN** exactly that file is published (source `bash`), and the already-dirty file emits nothing

#### Scenario: Reverts are published and commits are not

- **GIVEN** a baselined run with a dirty file
- **WHEN** the file is reverted (`git checkout --`) and a later bash call runs
- **THEN** the revert is published; when a commit instead leaves the content unchanged, nothing is published

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

The CLI SHALL provide a `lunate trust` command for scripted and CI runs, operating on the current working directory's repository identity and worktree. With no flags it SHALL print this repository's trust records, every pending decision — a project extension whose hash is unknown or changed, an untrusted project `mcp.json` file (any of the standard project locations), or a project-scoped server definition without its per-server approval — using the same content the interactive trust question shows (extension id, version, directory, and the changed-files note when re-prompting; for a config file the path with each server's name and command; for a per-server approval the server's name, source file and full command), and one machine-independent digest over all pending decisions; the digest SHALL cover pending content only — no repository identity, worktree path, extension directory or config path — so a digest pinned on any machine matches the same pending content anywhere; it SHALL record nothing. `--yes` SHALL print the same decisions and then record them in the store's sections (extension trust, the project `mcp.json` files and the per-server approvals), including a new worktree of a trusted repository, which a session would record without asking; a second invocation SHALL find nothing pending and succeed. `--yes --digest <sha>` SHALL recompute the digest and record only on an exact match; on mismatch it SHALL record nothing and show the difference. A pinned digest SHALL require a pending set to verify: with nothing pending it SHALL record nothing and exit 4, while unpinned `--yes` with nothing pending SHALL still succeed. `--json` SHALL be the inspect output contract: one JSON document carrying the records, the pending decisions and the digest, canonical newlines and invariant culture; combining it with `--yes` or `--clear` SHALL be a usage error. `--clear` SHALL clear the repository's records — the file records and the per-server approvals — and print what it removed. The command SHALL perform discovery and hashing only — it SHALL NOT load extension assemblies, dispatch hooks, or start MCP servers — and the hashes it records SHALL be the hashes it prints. Exit codes SHALL be 0 for success (inspect with nothing pending), 3 when inspect finds pending decisions, 4 on a digest mismatch, 2 for usage errors (unknown or conflicting flags), and 1 for unexpected failures.

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

#### Scenario: Per-server approvals are pending decisions too

- **GIVEN** a trusted project file whose servers have no per-server approvals
- **WHEN** `lunate trust` runs and then `lunate trust --yes`
- **THEN** inspect prints each approval with the server's name, source file and full command and exits 3, and `--yes` records them so a later session asks nothing

### Requirement: Trust command security posture

The `lunate trust` command SHALL be the only non-interactive way to record trust; no environment variable, configuration key or flag SHALL make a session record trust implicitly, and no console prompt SHALL return. The command SHALL print exactly what it records before recording it. For untrusted inputs the pinned form SHALL be the CI guidance: inspect prints a digest over the pending decisions, and `--yes --digest <sha>` records only when the recomputed digest matches — a mismatch records nothing, shows the difference, and exits non-zero. Plain `--yes` SHALL be documented as being for checkouts the operator controls, never for untrusted pull requests or forks. The project `mcp.json` trust hash SHALL be computed over each server's raw definition as written — before any `${VAR}` expansion — canonically serialized, so it is machine-independent and no expanded value or secret can influence it. Per-server approval records SHALL follow the same discipline — recorded only by an interactive prompt or the command, keyed by the repository identity and the same canonical definition hash, never influenced by expanded values. Records SHALL be documented as per user and machine: ephemeral CI runners SHALL run the trust step in every job.

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

### Requirement: The `/extensions` view

The CLI SHALL provide an interactive `/extensions` command and a `lunate extensions [--json]` subcommand listing every discovered extension — failed first, then trust-denied, then loaded, each group by id — with its id, version, scope (`global`/`project`) and state (`loaded`, `trust-denied` or `failed`). Loaded extensions SHALL show their load time in milliseconds (a load above 500 ms marked `(slow)`) and their **registered** tool, command, hook and service counts, a field differing from the manifest's declared list carrying `(N declared)`; trust-denied and failed extensions SHALL show the manifest's **declared** counts, and a failed extension's message SHALL be truncated to its first sentence followed by ` — see the log` when truncated. The interactive form SHALL render one segment per extension in the single-line notice discipline (segments joined `;`, prefixed `extensions: `); the subcommand SHALL print one line per extension with load times labelled `(CLI-measured)`, or with `--json` a machine-readable array of the same fields (`loadMs`, `slow` where applicable), and SHALL compose the host in register-only inspect mode — loading and registering trusted extensions but starting no session events, background services or MCP servers, unloading each extension and stopping the host before exit — and SHALL never prompt, so an extension it cannot trust reads `trust-denied`. It SHALL exit non-zero when any extension failed. With no extensions discovered, the interactive form SHALL read `extensions: none configured` and `--json` SHALL print `[]`; with extensions disabled (`--no-extensions`) the interactive form SHALL read `extensions disabled (--no-extensions)`. The view SHALL never print settings values, secrets or environment values, and numbers SHALL be formatted culture-invariantly.

#### Scenario: A session shows each extension's scope, state, load time and contributions

- **GIVEN** a session with one loaded global extension and one project extension whose trust was denied
- **WHEN** `/extensions` is submitted
- **THEN** one segment per extension appears ordered failed, trust-denied, loaded — the loaded one showing `(global) loaded in N ms` and its registered counts, the denied one showing `(project) trust-denied` and its declared counts

#### Scenario: The subcommand is register-only and never prompts

- **GIVEN** a store with one trusted global extension whose background service and session hooks write markers, and one untrusted project extension
- **WHEN** `lunate extensions` runs non-interactively
- **THEN** the factory ran (registration marker present) but no service-start or session-hook marker exists, nothing prompted, no trust was recorded, the host stopped, and the exit code is 0; with `--json` the same data prints as an array of objects

#### Scenario: A failed extension makes the subcommand exit non-zero

- **GIVEN** a store with an extension whose load fails
- **WHEN** `lunate extensions` runs
- **THEN** its line reads `failed` with the first sentence of the message and ` — see the log` when truncated, and the exit code is non-zero

#### Scenario: The empty and disabled views

- **GIVEN** no extensions are discovered (or the run carries `--no-extensions`)
- **WHEN** `/extensions` is submitted (or `lunate extensions` runs)
- **THEN** the interactive form reads `extensions: none configured` (`extensions disabled (--no-extensions)` when disabled) and `--json` prints `[]`

### Requirement: Skill sources from extensions

Extensions SHALL contribute skills through a new `ISkillSource` service registered like other extension services: an id plus an enumeration of skill directories **on disk inside the extension's own folder** (v1 is disk-only — no in-memory skills; the core scans and validates them exactly like any other skill). Contributed skills SHALL appear in the skills index and `/skills` tagged `extension '<id>'`, SHALL inherit the extension's trust (no separate question) **and its disable state** (`disable-model-invocation` honoured), and SHALL count under the contributing extension's context budget (enforced before the global index cap). A malformed descriptor SHALL warn like a core skill and SHALL NOT load. A shadowed extension skill follows the common collision rule: the winner carries the `shadows` marker, the one-time warning names it, and the extension copy is not listed separately. The ADR-0017 catalogue and `docs/spec/extensibility.md` SHALL gain the resource-discovery entry; the record lands as `adr/0017-addendum-skills.md`.

#### Scenario: A contributed skill joins the index tagged by source

- **GIVEN** a trusted extension registering an `ISkillSource` with one skill directory inside its folder
- **WHEN** a session starts
- **THEN** the index and `/skills` list the skill with `extension '<id>'` as its scope, and the read tool can read its files

#### Scenario: Over-budget contributions drop and log

- **GIVEN** an extension whose contributed index lines exceed its context budget
- **WHEN** the session starts
- **THEN** exceeding lines are dropped and an extension log entry names the extension and the budget

#### Scenario: An untrusted extension contributes nothing

- **GIVEN** an untrusted project extension registering a skill source
- **WHEN** the session starts
- **THEN** its skills are absent from the index and `/skills`

### Requirement: Out-of-process extension host

An extension whose manifest declares `runtime` SHALL run as a child process instead of an assembly load: discovery, scope and failed-extension containment SHALL be identical to in-process extensions, except that trust SHALL hash the manifest together with the extension directory contents (entry script and bundled files) — swapping a bundled file after trust SHALL re-prompt — and the trust prompt SHALL state that the extension runs with the user's privileges. The host SHALL build hook and service proxies from the manifest's declarations without starting anything, and SHALL start the process lazily on the extension's first use — never during discovery, load, registration or composition — through the core process capability; a manifest startup mode `background` SHALL instead start it after the interactive UI is ready, never on the startup path, and a subscribed `session-started` SHALL be delivered when the session is live (the same post-ready point) or on the child's first successful start, whichever comes first, so no child ever starts on the startup path. A relative `command` or argument path SHALL resolve against the extension directory and the default working directory SHALL be the extension directory. The child's environment SHALL be exactly the minimal base set (PATH, HOME/USERPROFILE, SYSTEMROOT, TEMP/TMP/TMPDIR, LANG) plus `envPassthrough` plus `env`, with no inheritance — Lunate's provider keys SHALL NOT leak — built by the shared scrubber (the code-mode sample's implementation, promoted into the contract assembly; one implementation). The wire SHALL be newline-delimited JSON-RPC 2.0: UTF-8, no BOM, no batches; stdout SHALL be protocol-only — a non-JSON line is a protocol error and a line over 16 MB is a protocol error, never an OOM — and stderr SHALL be captured into the extension log, capped. The first call SHALL be an `initialize` request carrying `protocolVersion` and capabilities, the manifest echo, the workspace info and the declared hooks and services; a version mismatch SHALL refuse the extension with a clear status — never best effort — and the result SHALL match the manifest: a hook or service the child implements but the manifest does not declare SHALL be refused, a declaration the child does not implement SHALL be warned (the in-process rules, unchanged). The channel SHALL be bidirectional and reentrant — the child may call host services while handling a hook — with separate id spaces per direction and concurrent requests allowed. Cancellation SHALL map `$/cancelRequest` notifications to `CancellationToken` in both directions. The host SHALL forward only the hooks the manifest subscribes to: observer hooks (`SessionStarted`, `SessionEnding`, `ProviderStreamEvent`, `RunSettled`, `ModelChanged`, `ToolsChanged`, `FileChanged`) SHALL be notifications — fire-and-forget, and `ProviderStreamEvent` deltas SHALL not be forwarded unless explicitly subscribed — while interceptor hooks (`ProjectTrust`, `InputReceived`, `RunStarting`, `ContextBuilding`, `MessageCompleted`, `ToolCalling`, `ToolResultReady`, `TurnEnded`, `Compacting`) SHALL be requests. Timeouts and failures SHALL follow the catalogue: observers drop with a warning; interceptors take the hook's no-opinion semantics (keep original / skip) with a warning, except the gating hooks — `ToolCalling` blocks fail-safe and `ProjectTrust` denies. Every call SHALL carry the run's ids (RunId, and ParentRunId when present) and the workspace; one child SHALL serve the whole session, subagent and worktree runs included. Service calls SHALL cross both ways — consumption by name through the registry, and calls on services the remote extension registered — as JSON-RPC requests with JSON arguments and results, and services a remote extension registers SHALL be async-only (in-process consumers SHALL NOT call them on a synchronous hot path). A process that exits SHALL fail in-flight calls immediately, without waiting for their timeouts, with one notice, flip the extension to a failed status with the reason `the extension process exited`, and the session SHALL continue; the host SHALL attempt at most one automatic restart per session, after which the extension reads failed until a manual restart from `/extensions`. At session end the host SHALL send the shutdown request, allow a bounded grace, then kill the process tree — a Job Object on Windows, the children-first POSIX walk on Unix. The process boundary SHALL provide runtime isolation — dependencies, language, crash containment — and SHALL NOT be described or treated as a permission boundary.

#### Scenario: A remote extension starts lazily and answers a hook

- **GIVEN** an installed remote extension whose manifest declares a hook
- **WHEN** the hook first fires in a session
- **THEN** the process starts, the handshake succeeds and the hook's result crosses back and takes effect

#### Scenario: A protocol version mismatch refuses

- **GIVEN** a remote extension whose `initialize` result reports a different protocol version
- **WHEN** the extension first runs
- **THEN** it is refused with a clear status, never driven best-effort, and the session continues

#### Scenario: A hook handler calls back into the host

- **GIVEN** a started remote extension handling a hook
- **WHEN** its handler calls a host service before answering
- **THEN** the service call is answered while the hook call is in flight, and the hook's result still lands

#### Scenario: Declarations and the initialize result must agree

- **GIVEN** a remote extension whose initialize result implements a hook its manifest does not declare
- **WHEN** the handshake completes
- **THEN** the extension is refused; a declared-but-unimplemented hook only warns

#### Scenario: A timed-out gating hook fails closed

- **GIVEN** a remote `ToolCalling` handler that does not answer within its timeout
- **WHEN** the tool call dispatches
- **THEN** the call is blocked with a reason and the failure is reported

#### Scenario: A crash fails fast and restarts at most once

- **GIVEN** a started remote extension with a hook call in flight
- **WHEN** its process exits
- **THEN** the in-flight call fails immediately with one notice, at most one automatic restart is attempted, and after that the extension reads failed until a manual restart from `/extensions`

#### Scenario: The child environment is exactly base plus passthrough plus env

- **GIVEN** a remote extension whose manifest declares one environment variable and one passthrough name, and a parent process holding a provider key
- **WHEN** the child starts
- **THEN** the declared variable and passthrough name are present, the provider key is absent, and nothing else is inherited

#### Scenario: Session end stops the process within the grace

- **GIVEN** a started remote extension
- **WHEN** the session ends
- **THEN** the shutdown request is sent, the process exits within the grace window, and a process that ignores it is killed children-first

#### Scenario: A non-JSON stdout line is a protocol error

- **GIVEN** a remote extension that writes a non-JSON line to stdout
- **WHEN** the host reads it
- **THEN** it is treated as a protocol error, never as protocol data

#### Scenario: A background extension starts after the UI is ready

- **GIVEN** a remote extension whose manifest declares the `background` startup mode
- **WHEN** an interactive session starts
- **THEN** the process starts only after the UI is ready, and the startup path and its budget are untouched

#### Scenario: Trust covers the extension directory

- **GIVEN** a trusted remote extension whose bundled entry script changes
- **WHEN** the next session starts
- **THEN** the trust prompt fires again

### Requirement: Remote wire conformance

Every hook payload and result DTO and the service-call envelope SHALL round-trip over the actual wire — serialized, framed as one JSON-RPC message, parsed and compared by the peer — with the inventory covering the hook catalogue exhaustively so a new DTO cannot ship without a wire round-trip. A JSON Schema generated from the hook and service DTOs SHALL be published with the docs and shipped in `Lunate.Extensibility.Testing`, so non-C# authors do not reverse-engineer C#. `Lunate.Extensibility.Testing` SHALL provide a conformance runner that can be pointed at any remote extension executable. The node reference extension SHALL live in the fitness suite (`samples/extensions`, its tests running in CI like every sample), and a production composition test SHALL load a remote extension through the real host — hook delivered, service call answered.

#### Scenario: A new DTO without a wire round-trip fails conformance

- **GIVEN** a hook payload DTO added to the catalogue without a wire round-trip entry
- **WHEN** the conformance suite runs
- **THEN** it fails naming the DTO

#### Scenario: The conformance runner checks an arbitrary executable

- **GIVEN** a remote extension executable that speaks the protocol
- **WHEN** the conformance runner is pointed at it
- **THEN** it performs start, handshake, a hook round-trip, a service call in each direction and shutdown, and reports the outcome

#### Scenario: The reference extension is wired through the real host

- **GIVEN** the node reference extension installed in a test workspace
- **WHEN** a composed session runs a tool call
- **THEN** its hook observes the call, its registered service answers a host call, and its service consumption receives a result

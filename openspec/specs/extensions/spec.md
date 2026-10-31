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

The host SHALL discover extensions in `~/.lunate/extensions/` (global) and `.lunate/extensions/`
(project), reading manifests only — assemblies SHALL load lazily on first use into a collectible
`AssemblyLoadContext` that shares the contract assemblies — `Lunate.Extensibility.Abstractions` and
the `Lunate.Agent` types it references — and `Microsoft.Extensions.AI.Abstractions` and
`System.Text.Json` with the default context, and isolates every other dependency. Host-supplied
built-in descriptors SHALL be folded in before the two directory scans (see *Built-in extension
provenance*), in the composition's deterministic order. Unload SHALL be idempotent; a later load
SHALL work; state SHALL not survive. Duplicate ids across scopes SHALL be an error naming both
paths, and a discovered id equal to a built-in id SHALL be skipped naming both sources. Missing
dependencies SHALL yield an actionable error. The CLI SHALL compose the host when a session starts —
the host's first use — for interactive, print and ACP sessions; `--version`, `--help` and
non-session commands SHALL NOT read the extension store. `--no-extensions` SHALL compose only the
built-in set, with no store scan, no third-party extension, and no third-party extension MCP server,
renderer or widget; configured `mcp.json` servers are unaffected. A discovery or load failure SHALL be logged
through the host log and SHALL NOT abort the session.

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
- **THEN** the extension is discovered and loaded as the session starts, and its hook observes the
  session's tool calls

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
- **THEN** the store is not read, the installed extension's hook never runs, the built-in set is the
  only extension content composed and runs, and the configured MCP server still works

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

Project extensions SHALL require a one-time trust decision per repository identity (host-provided, `GitCommonDir` per the worktree architecture), re-prompted when the extension content hash changes for a worktree. New worktrees of a trusted repository SHALL not re-prompt. Denial SHALL refuse the load. Hand-placed global extensions SHALL not be gated, while an extension whose directory an install record owns SHALL be pending in either scope: approving it through the existing question or `lunate trust` SHALL be what makes it load, and the decision SHALL bind the package identity beside the directory content hash, in the existing extensions section rather than a new record shape. Content hashes SHALL be computed over the extension directory's files deterministically, and trust records SHALL be written atomically. An extension installed by `lunate install` SHALL bind, besides the directory content hash, the package that provided it — the canonical package id, the exact version, the nupkg's SHA-512 and the canonical dependency-lock graph — so an upgraded package re-prompts exactly as changed files do, and an edit inside the installed directory still re-prompts through the content hash. The question for such an extension SHALL name its source package on one line, `from package <id>@<version> (<source>)`. In interactive sessions the decision SHALL be asked as a live-area prompt answered with keys through the normal key path (deny by default); print and ACP sessions SHALL refuse an untrusted project extension with a logged hint naming the approval paths — an interactive session or the `lunate trust` command. A re-prompt caused by changed files SHALL say so. After any answer, one notice SHALL state that the decision is remembered for this repository and how to change it: `/trust` lists this repository's records and `/trust clear` clears them, so the next load asks again.

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

#### Scenario: An installed extension's question names its package

- **GIVEN** an untrusted repository with a project install of `Acme.Toolkit@1.4.2` from `nuget.org`
- **WHEN** a session starts and the extension question renders
- **THEN** the question names the extension id, version and directory and one line reads `from package Acme.Toolkit@1.4.2 (nuget.org)`

#### Scenario: An upgraded package re-prompts

- **GIVEN** a trusted project install of `Acme.Toolkit@1.4.2`
- **WHEN** `lunate install Acme.Toolkit@1.5.0 --project` runs and a session starts
- **THEN** the question is asked again, naming `Acme.Toolkit@1.5.0`

#### Scenario: An edit inside an installed extension re-prompts

- **GIVEN** a trusted project install whose recorded entry assembly is edited by hand
- **WHEN** a session starts
- **THEN** the content hash no longer matches and the question is asked again

#### Scenario: A user-scope install asks before it loads

- **GIVEN** an extension installed at the user scope by `lunate install`, and no decision for it
- **WHEN** a session starts
- **THEN** the extension question is asked for it in the user scope with `from package <id>@<version> (<source>)`, the extension does not load until it is approved, and a later session asks nothing

#### Scenario: A hand-placed user-scope extension is not gated

- **GIVEN** an extension directory under `<root>/extensions/` that no install record names
- **WHEN** a session starts
- **THEN** it loads with no question

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
Each handler SHALL run under a per-handler timeout (configurable; `ProviderStreamEvent` on a shorter fast path). Every dispatch SHALL be timed with a monotonic clock and aggregated per (extension, hook) for the session — call count, total, last and maximum milliseconds, and separate completed, timed-out and cancelled counters — thread-safely and without per-dispatch allocation, with child runs counted into the same session aggregate, and the elapsed time SHALL also be recorded to a `System.Diagnostics.Metrics` histogram on meter `Lunate.Extensibility` with `extension` and `hook` tags. A cancelled dispatch (user cancellation, run teardown) SHALL be counted as cancelled and SHALL NOT feed the maximum, the slow count or the warning — its elapsed time reflects the user; a timeout counts as the hook's cost. A dispatch above the slow threshold SHALL only be reported as slow when the machine is not the reason it crossed: while more work waits for the thread pool than the pool has workers, a continuation — the handler's own included — waits for a thread instead of running, so a dispatch that took that long SHALL be counted as a call and SHALL NOT feed the maximum, the slow count or the warning, and SHALL NOT set the warn-once latch (the place a cancelled dispatch already sits), so a loaded machine cannot accuse a hook that was never slow and the first crossing on an idle machine still warns. The pool's state SHALL be sampled when the dispatch starts and again when it settles; a dispatch either sample finds behind is counted without feeding the maximum, the slow count or the warning, and without spending the warn-once latch — a pool that was behind when the handler was called did not measure the handler, whatever it looks like by the time the result is recorded. The first dispatch above the slow threshold (configurable, default 500 ms) SHALL log one warning naming the extension, the hook, the elapsed time and the threshold and pointing at `/extensions` — a dim notice in the interactive UI, log-only in headless and protocol modes, never stdout — and later crossings SHALL only increment the slow count. A remote extension's interceptor hook SHALL be timed at the same point, so its recorded time includes the child-process round trip; a remote observer hook goes out as a notification with no round trip, so its recorded time is the host-side send time, labeled as such, and the lazy start and `initialize` handshake SHALL be excluded from the first dispatch's timing, recorded separately as the extension's start duration. Continuations requested at turn boundaries SHALL be capped per run (default 3); requests beyond the cap SHALL be ignored and logged.

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

#### Scenario: A crossing the loaded pool explains is not reported

- **GIVEN** a fast handler whose continuation a loaded thread pool delays past the slow threshold
- **WHEN** the dispatch settles
- **THEN** it counts as a call, does not feed the maximum, the slow count or the warning, and no
  notice is raised — while the same crossing on an idle pool warns once

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

The CLI SHALL provide a `lunate trust` command for scripted and CI runs, operating on the current working directory's repository identity and worktree. With no flags it SHALL print this repository's trust records, every pending decision — a project extension whose hash is unknown or changed, an untrusted project `mcp.json` file (any of the standard project locations), or a project-scoped server definition without its per-server approval — using the same content the interactive trust question shows (extension id, version, directory, and the changed-files note when re-prompting; for a config file the path with each server's name and command; for a per-server approval the server's name, source file and full command), and one machine-independent digest over all pending decisions; the digest SHALL cover pending content only — no repository identity, worktree path, extension directory or config path — so a digest pinned on any machine matches the same pending content anywhere; it SHALL record nothing. `--yes` SHALL require an interactive terminal — a redirected stdin SHALL be refused — and SHALL print the same decisions and then record them in the store's sections (extension trust, the project `mcp.json` files and the per-server approvals), including a new worktree of a trusted repository, which a session would record without asking; a second invocation SHALL find nothing pending and succeed. `--yes --digest <sha>` SHALL NOT require an interactive terminal, because it names the exact content it approves, and SHALL recompute the digest and record only on an exact match; on mismatch it SHALL record nothing and show the difference. A pinned digest SHALL require a pending set to verify: with nothing pending it SHALL record nothing and exit 4, while unpinned `--yes` with nothing pending SHALL still succeed. `--json` SHALL be the inspect output contract: one JSON document carrying the records, the pending decisions and the digest, canonical newlines and invariant culture; combining it with `--yes` or `--clear` SHALL be a usage error. `--clear` SHALL clear the repository's records — the file records and the per-server approvals — and print what it removed. The command SHALL perform discovery and hashing only — it SHALL NOT load extension assemblies, dispatch hooks, or start MCP servers — and the hashes it records SHALL be the hashes it prints. Exit codes SHALL be 0 for success (inspect with nothing pending), 3 when inspect finds pending decisions, 4 on a digest mismatch, 2 for usage errors (unknown or conflicting flags), and 1 for unexpected failures.

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

#### Scenario: `--yes` without a terminal is refused
- **GIVEN** a repository with a pending decision and no interactive terminal
- **WHEN** `lunate trust --yes` runs
- **THEN** it records nothing, names the terminal requirement, and exits 2

#### Scenario: `--yes --digest` runs without a terminal
- **GIVEN** a repository with a pending decision and no interactive terminal
- **WHEN** `lunate trust --yes --digest <the pending digest>` runs
- **THEN** it records the decision and exits 0

### Requirement: Trust command security posture

The `lunate trust` command SHALL be the only non-interactive way to record trust; no environment variable, configuration key or flag SHALL make a session record trust implicitly, and no console prompt SHALL return. The command SHALL print exactly what it records before recording it. For untrusted inputs the pinned form SHALL be the CI guidance: inspect prints a digest over the pending decisions, and `--yes --digest <sha>` records only when the recomputed digest matches — a mismatch records nothing, shows the difference, and exits non-zero. Plain `--yes` SHALL be documented as being for checkouts the operator controls, never for untrusted pull requests or forks. The project `mcp.json` trust hash SHALL be computed over each server's raw definition as written — before any `${VAR}` expansion — canonically serialized, so it is machine-independent and no expanded value or secret can influence it. Per-server approval records SHALL follow the same discipline — recorded only by an interactive prompt or the command, keyed by the repository identity and the same canonical definition hash, never influenced by expanded values. Records SHALL be documented as per user and machine: ephemeral CI runners SHALL run the trust step in every job.

#### Scenario: Expanded values never enter the hash

- **GIVEN** a project `mcp.json` whose definitions reference environment variables
- **WHEN** its trust is recorded with any environment values
- **THEN** the recorded hash depends only on the raw definitions and no secret value appears in the trust file

### Requirement: Extension tools

Extensions SHALL register tools through `IExtensionContext.RegisterTool` at creation time only —
registration SHALL NOT start processes, sockets or timers — with a registration carrying a local
name, a description, a hand-written JSON `ParametersSchema`, optional `OutputSchema`, optional
`ToolAnnotations`, a `ToolExposure` (Direct default), an optional explicit `ToolRisk` override
(otherwise derived from the annotations as for core tools) and an `IExtensionToolHandler`. The
handler SHALL receive the same `ToolContext` and return the same `ToolResult` as core tools — one
tool model, including `ctx.FileChanges`, `ctx.RunId`, `ctx.WorkingDirectory` and nested
`ExecuteToolAsync` with `"<parent>/<n>"` ids and the core's depth limits. The model-facing name SHALL
be the manifest alias when one is declared, otherwise `<extension-id>__<local-name>`, sanitized to
`[A-Za-z0-9_-]` and at most 64 characters (no `:`); core tool names stay reserved; the namespace
(discovery grouping) SHALL be the extension id. Names SHALL be unique: duplicates within the
extension, across extensions, or against a core name SHALL be refused naming both sources. A
composition-time collision between an extension tool and an MCP tool's model-facing name SHALL
resolve first-composition-wins: extension tools compose before MCP tools, and the later tool SHALL be
skipped with one single-line diagnostic naming both sources; malformed declarations (blank or
invalid name, non-object schema, missing handler) SHALL be refused naming the extension and the
field; registrations per extension SHALL be bounded at 32. The manifest `tools` list SHALL be
enforced asymmetrically: a registered tool that the manifest does not declare SHALL refuse the load
(the manifest is what the user trusted before any code loaded), while a declared tool that is not
registered SHALL be logged as a warning and dropped from the offered set. A tool's declared
`toolActivation` rule SHALL decide whether it enters a run's registry (see *Conditional tool
availability*). Registered tools SHALL enter the same `ToolRegistry` as core tools and behave
identically: the exposure gate governs declaration and nested/programmatic execution, annotations
reach approval, a throwing handler becomes a teaching error result, and unload removes the tool and
cancels in-flight calls into a cancelled result.

#### Scenario: A registered tool is declared and runs

- **GIVEN** an extension registering a tool with a hand-written schema
- **WHEN** a session composes and the model calls the tool
- **THEN** the declaration carries the prefixed name (`<extension-id>__<name>`), the description and
  the authored schema, and the handler executes with the model's arguments and the real
  `ToolContext`

#### Scenario: An alias replaces the model-facing name

- **GIVEN** an extension tool with a declared alias
- **WHEN** the declaration is built
- **THEN** the alias is the model-facing name, while the extension id and local name remain the
  identity used by diagnostics, grouping and unload

#### Scenario: Annotations reach the ToolCalling hook

- **GIVEN** an extension tool declared `Destructive`
- **WHEN** a call to it dispatches
- **THEN** the `ToolCalling` payload lists the annotations and a policy handler can block the call
  on that basis

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
- **THEN** the load is refused; and a declared but unregistered tool name is logged as a warning and
  dropped from the offered set

#### Scenario: A colliding MCP tool is skipped with a diagnostic

- **GIVEN** an extension tool and an MCP tool whose model-facing names collide
- **WHEN** the session composes tools
- **THEN** the extension tool is registered, the MCP tool is skipped with one single-line diagnostic
  naming both sources, and the session continues

#### Scenario: The context publishes file changes and runs nested calls

- **GIVEN** an extension tool whose handler publishes a file change and makes a nested call
- **WHEN** it executes
- **THEN** the publish lands on the bus with the full payload (absolute and relative path, workspace
  id, run id, kind) and the nested call runs through the same pipeline with a `"<parent>/<n>"` id

#### Scenario: A handler failure teaches, unload cancels

- **GIVEN** a handler that throws, and a second call in flight when the extension unloads
- **WHEN** they settle
- **THEN** the throw becomes an error result with instructing text (never a crash), and the in-flight
  call is cancelled and settles as a cancelled result while the extension's tools leave the registry
  with `ToolsChanged` fired

#### Scenario: A resumed session renders a missing tool

- **GIVEN** a resumed session containing a call to an extension tool that is not present
- **WHEN** the call is rendered
- **THEN** it renders through the default view without a registered renderer and without a crash

#### Scenario: An inactive tool is absent from the run

- **GIVEN** a declared extension tool whose activation rule is false for the run's workspace
- **WHEN** the run's tool registry is composed
- **THEN** the tool is not declared, not nested-callable and absent from the programmatic stubs, while
  it remains registered in the extension registry and visible in `/extensions` as inactive

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

The CLI SHALL provide an interactive `/extensions` command and a `lunate extensions [--json]` subcommand listing every discovered extension — failed first, then trust-denied, then loaded, each group by id — with its id, version, scope (`global`/`project`/`builtin`) and state (`loaded`, `trust-denied` or `failed`). Loaded extensions SHALL show their load time in milliseconds (a load above 500 ms marked `(slow)`) and their **registered** tool, command, hook and service counts, a field differing from the manifest's declared list carrying `(N declared)`; trust-denied and failed extensions SHALL show the manifest's **declared** counts, and a failed extension's message SHALL be truncated to its first sentence followed by ` — see the log` when truncated. The interactive form SHALL render one segment per extension in the single-line notice discipline (segments joined `;`, prefixed `extensions: `); the subcommand SHALL print one line per extension with load times labelled `(CLI-measured)`, or with `--json` a machine-readable array of the same fields (`loadMs`, `slow` where applicable), and SHALL compose the host in register-only inspect mode — loading and registering trusted extensions but starting no session events, background services or MCP servers, unloading each extension and stopping the host before exit — and SHALL never prompt, so an extension it cannot trust reads `trust-denied`. It SHALL exit non-zero when any extension failed. A built-in extension SHALL appear with scope `builtin`, state `loaded`, its registered counts, and never `pending` or `trust-denied`; the interactive form SHALL offer an unload/reload action for it and `lunate extensions reload <id>` and unloading by id SHALL be refused with a teaching message naming the built-in and the reason (a built-in is process-lifetime code with no ALC to release). With no extensions discovered, the interactive form SHALL read `extensions: none configured` and `--json` SHALL print `[]`; with extensions disabled (`--no-extensions`) the interactive form SHALL read `extensions disabled (--no-extensions)` and the composed built-in segments (scope `builtin`, state `loaded`) SHALL follow the same reading and listing as with extensions enabled. A loaded extension's enabled tool state SHALL be visible: a declared tool whose `toolActivation` rule is false for the run's workspace SHALL be reported as `inactive (<condition>): <reason>` after the tool counts, and a true rule SHALL count as active, with the registered/declared tool counts unchanged by the rule. The view SHALL never print settings values, secrets or environment values, and numbers SHALL be formatted culture-invariantly.

#### Scenario: A session shows each extension's scope, state, load time and contributions

- **GIVEN** a session with one loaded global extension and one project extension whose trust was denied
- **WHEN** `/extensions` is submitted
- **THEN** one segment per extension appears ordered failed, trust-denied, loaded — the loaded one showing `(global) loaded in N ms` and its registered counts, the denied one showing `(project) trust-denied` and its declared counts

#### Scenario: A built-in segment cannot be unloaded or reloaded

- **GIVEN** a session with one composed built-in extension
- **WHEN** `/extensions` is submitted and unload or reload is attempted for its id
- **THEN** the segment reads `(builtin) loaded` with its registered counts, the attempt is refused with a teaching message naming the built-in, and the built-in stays loaded

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
- **THEN** the interactive form reads `extensions: none configured` (`extensions disabled (--no-extensions)` when disabled) and `--json` prints `[]`, and with a built-in composed under `--no-extensions` the built-in segments follow the same reading and listing as with extensions enabled

#### Scenario: An inactive tool is reported with its rule

- **GIVEN** a loaded extension whose manifest declares a tool with a false activation rule and a tool with a true rule
- **WHEN** `/extensions` is submitted (or `lunate extensions` runs)
- **THEN** the false-rule tool reads `inactive (<condition>): <reason>` and the registered/declared tool counts are unchanged for both tools

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

### Requirement: Command registry

The CLI SHALL compose one command registry from ordered command sources. A source SHALL expose a `SourceId`, `DescribeAsync(CancellationToken)` returning its `CommandDescriptor` list, and `InvokeAsync(CommandInvocation, CancellationToken)` returning a `CommandResult`. A descriptor SHALL carry a bare name (no slash), a description, an optional argument hint, the source id and a kind of `Builtin`, `Skill`, `Template` or `Extension`. Invocation SHALL carry the bare name and the argument remainder. The registry SHALL order entries by kind — built-ins, then skills, then extensions, then templates — then by source registration order, then by descriptor order, and SHALL keep the first entry for a duplicate name while logging one diagnostic naming both sources. The registry SHALL be the only input to `/` autocomplete, dispatch and `/help`. Built-in commands SHALL be the first source and SHALL keep their names, `/help` grouping, descriptions, busy-session refusals and notices. A skill SHALL be described as `skill:<name>` and invoked through the existing skill path, so `/skill:<name>` keeps its today's wrapping and notices. The registry SHALL invoke handlers asynchronously with the caller's cancellation token; an `OperationCanceledException` for a cancelled token SHALL propagate, while any other handler exception SHALL become an error notice and a log entry naming the command and source, never a crash. A source that throws while describing SHALL be logged and skipped for that composition. The new public contract SHALL move the extension API version as a semver-minor addition. `/help` SHALL list every runnable command in the registry, not only built-ins: the existing built-in groups first and unchanged, then one section per non-built-in kind that has commands (`skills`, `extensions`, `templates`) in the registry's kind order, each section sorted by the descriptor's name and omitted when it has no rows, with rows carrying the typed name and the description and the `/skill:<name>` placeholder staying in its built-in group; a session with no non-built-in commands SHALL print the built-in listing exactly as before. Every entry SHALL carry one source-qualified id — `builtin:<name>`, `skill:<name>`, `template:<name>` or `ext:<extension id>:<name>` — computed by the host from the descriptor's kind, source and name, and used by the duplicate diagnostic, every log line and every failure notice beside the source id, so that two sources offering one name stay distinguishable; the id SHALL NOT change the typed command text or an autocomplete row, and a source SHALL NOT be able to set it. A handler failure SHALL be reported as a teaching error notice naming the command's id, its source, the reason and one recovery step (`/extensions` for an extension command, `/skills` for a skill), SHALL carry no stack trace, and SHALL leave the session running.

#### Scenario: Built-ins are first and unchanged

- **GIVEN** a composed registry
- **WHEN** the built-in descriptors are compared with the built-in table
- **THEN** they appear first, in table order, with the same names, descriptions and `/help` groups as before

#### Scenario: Source order is deterministic and first source wins

- **GIVEN** sources contributing commands of different kinds and a duplicate name across two sources
- **WHEN** the registry composes
- **THEN** descriptors are ordered built-ins, skills, extensions, templates, the duplicate appears once, and the first source after that order owns it

#### Scenario: Autocomplete, dispatch and help all read the registry

- **GIVEN** a source contributing a command that is not a built-in
- **WHEN** the command is completed, submitted and listed by `/help`
- **THEN** completion and dispatch use the registry's entry, and `/help` reflects the registry's built-in entries

#### Scenario: A throwing handler becomes a notice and a log entry

- **GIVEN** a registered command whose handler throws
- **WHEN** the command is invoked
- **THEN** the session receives an error result, renders an error notice, logs the command and source, and the session continues

#### Scenario: Cancellation reaches the handler

- **GIVEN** a registered command handler awaiting the invocation token
- **WHEN** the caller cancels
- **THEN** the handler observes cancellation and the cancellation propagates

#### Scenario: The skill invocation keeps its behaviour

- **GIVEN** a valid trusted skill and an unknown skill name
- **WHEN** `/skill:<name>` is submitted for each
- **THEN** the valid one wraps its body through the normal prompt path and the unknown one keeps the existing unknown-skill notice

#### Scenario: Help lists every source's commands

- **GIVEN** a composed registry holding a built-in, a skill and an extension command
- **WHEN** `/help` is submitted
- **THEN** the built-in groups print first and unchanged, then a `skills` section and an `extensions` section listing their commands

#### Scenario: An empty source adds no help section

- **GIVEN** a composed session with no skills and no extension commands
- **WHEN** `/help` is submitted
- **THEN** the listing is the built-in groups exactly as before, with no `skills`, `extensions` or `templates` heading

#### Scenario: Every command has a source-qualified id

- **GIVEN** a built-in `/help` and two extensions `alpha` and `beta` whose sources both provide `ping`
- **WHEN** the registry composes
- **THEN** the ids read `builtin:help`, `ext:alpha:ping` and `ext:beta:ping`, they are distinct, and the duplicate diagnostic names both sources and both `ping` ids

#### Scenario: A failing handler names the command and the recovery step

- **GIVEN** a loaded extension command whose handler throws
- **WHEN** it is submitted
- **THEN** the error notice names the command's id and source, the reason and `/extensions`, prints no stack trace, and the session continues

### Requirement: Extension commands

The extension contract SHALL let an extension register command sources through `IExtensionContext.RegisterCommandSource` at creation time only, with no processes, sockets or timers started by registration. A manifest `commands` entry SHALL be a declaration of an extension command; a command SHALL be runnable only when a registered source provides it and the manifest declares it. A provided command that the manifest does not declare SHALL refuse the load. A declared command that no registered source provides SHALL be logged and shown by `/extensions` as `declared, not provided`, and SHALL NOT appear in autocomplete or dispatch. A provided command whose name equals a built-in command SHALL refuse the load with a message naming the extension and the built-in. An invalid provided name (empty, whitespace or a leading slash) SHALL refuse the load naming the field. Described commands SHALL be cached at load, re-stamped with the extension id as source and the `Extension` kind, and removed with the extension on unload. An invocation SHALL be async and cancellable; a throwing handler SHALL become an error notice plus a log entry, never a crash. Remote extensions SHALL provide their declared commands over the wire when the child advertises the `commands` capability (see *Remote command invocation*); a remote child that does not advertise it SHALL have every declared command reported as declared, not provided, and SHALL omit them from autocomplete and dispatch. The new context method SHALL move the extension API version as a semver-minor addition. An extension command's id SHALL be `ext:<extension id>:<bare name>`, stamped at load beside the source id and the `Extension` kind, and SHALL be the identity every diagnostic, log line and failure notice uses, so two extensions declaring one name stay distinguishable even though the bare name still dispatches to the first entry after ordering. An invocation SHALL hold a lease on its extension for its duration: unloading an extension with a live invocation SHALL cancel that invocation's token, SHALL report it to the user as a notice naming the extension and the command id, and SHALL wait a bounded time for the handler to return before the load context unloads; a handler that ignores cancellation SHALL be logged once and SHALL NOT hold the unload open beyond that bound. An unload SHALL NOT be reported as complete while a handler it cancelled is still running.

#### Scenario: A declared and provided extension command dispatches

- **GIVEN** an extension whose manifest declares `ping` and whose source provides `ping`
- **WHEN** `/ping hello` is submitted in a composed session
- **THEN** the extension handler receives `ping` with argument `hello`, the result is shown as a notice, and no model call is made

#### Scenario: A declared but unprovided command is not runnable

- **GIVEN** an extension whose manifest declares `ghost` and whose code registers no source providing it
- **WHEN** the session composes and `/extensions` is submitted
- **THEN** the load succeeds, the log names `ghost`, `/extensions` reads `declared, not provided`, `/` does not offer it, and submitting it produces the unknown-command notice

#### Scenario: A built-in name collision is refused at load

- **GIVEN** an extension providing a command named `help` while `help` is a built-in command
- **WHEN** the extension loads
- **THEN** the load is refused with a message naming the extension and the built-in command

#### Scenario: A provided but undeclared command refuses the load

- **GIVEN** an extension whose source provides `surprise` while the manifest declares no such command
- **WHEN** the extension loads
- **THEN** the load is refused with a message naming the extension, the command and the manifest rule

#### Scenario: Unload removes extension commands

- **GIVEN** a loaded extension contributing a command
- **WHEN** the extension unloads
- **THEN** the command leaves the registry, autocomplete and dispatch

#### Scenario: A throwing extension handler is contained

- **GIVEN** a loaded extension command whose handler throws
- **WHEN** the command is submitted
- **THEN** the session shows an error notice, the log names the extension and command, and later commands still work

#### Scenario: Two extensions may declare the same command name

- **GIVEN** extensions `alpha` and `beta` whose sources both provide `ping`
- **WHEN** the registry composes and `/ping` is submitted
- **THEN** both extensions load, `/ping` dispatches to `alpha` (first after ordering), and the log records the shadowed `ext:beta:ping`

#### Scenario: Unload cancels an in-flight command

- **GIVEN** a loaded extension command whose handler awaits its token
- **WHEN** the extension is unloaded while the command runs
- **THEN** the handler observes cancellation, the session shows a notice naming the extension and the command id, and the unload returns after the handler leaves

#### Scenario: A handler that ignores cancellation does not block unload

- **GIVEN** an extension command handler that swallows its cancellation token
- **WHEN** the extension is unloaded while the command runs
- **THEN** the log records the unresponsive handler with its command id and the unload returns inside the bound

### Requirement: Template command source slot

The registry SHALL admit a prompt-template source without a second seam. A source describing `CommandKind.Template` descriptors SHALL compose like every other source, SHALL order after extension commands, SHALL describe ids as `template:<name>`, and SHALL appear in `/help` under `templates`. A template command whose typed name equals a built-in command SHALL lose the duplicate rule, SHALL not be listed or dispatched, and the diagnostic SHALL name both the built-in id and the template id, so a template can never shadow a built-in. This requirement SHALL NOT be read as shipping template discovery: the slot is exercised by a test source only, and a template source SHALL add no public type to the extension contract.

#### Scenario: A template source composes after extensions

- **GIVEN** a fixed built-in source, a skill source, an extension source and a template source
- **WHEN** the registry composes
- **THEN** the template entries come last, carry `template:<name>` ids, and appear under `templates` in `/help`

#### Scenario: A template cannot shadow a built-in

- **GIVEN** a template source offering `help` while `/help` is a built-in
- **WHEN** the registry composes
- **THEN** the template entry is dropped, the diagnostic names `builtin:help` and `template:help`, and the built-in still dispatches

### Requirement: Remote command invocation

The wire SHALL carry a `command/invoke` request from host to child, and the child SHALL answer it with a result or a failure. The payload SHALL be a wire DTO owned by the remote host package carrying the bare command name, the argument remainder and the invocation's request id — never a serialisation of the public invocation or result types — and the reply SHALL mirror exactly the output text, the optional failure message and the disposition of the in-process result. A field added to the abstract type SHALL require the DTO and the conformance run to move with it.

Cancellation SHALL reuse the existing `$/cancelRequest` with the invoke request id; no second cancel path SHALL exist. The child SHALL pass that cancellation to the handler's token, and a cancelled invocation SHALL reply as cancelled rather than failed.

An invocation SHALL hold the same lease on the extension as an in-process one: unloading cancels it, the cancel is reported to the user as a notice naming the extension and the command id, and the unload waits a bounded time for the reply. A throwing or failed remote handler SHALL become the same error notice shape as an in-process failure, naming the source-qualified command id, and SHALL NOT crash the session.

The child SHALL advertise a `commands` capability in `initialize`. A child that does not advertise it SHALL have its declared commands treated as declared-not-provided, omitted from autocomplete and dispatch. Adding the method SHALL raise the wire protocol version to 2; a version-1 child SHALL remain readable for hooks and services.

#### Scenario: A manifest command completes over the wire

- **GIVEN** a remote extension whose manifest declares `ping` and whose child advertises the `commands` capability
- **WHEN** the user submits `/ping hello`
- **THEN** the child receives the bare name `ping` with the remainder `hello`, its reply reaches the transcript as the command's output, and the notice names `ext:<id>:ping`

#### Scenario: Cancel mid-invocation is honoured across the wire

- **GIVEN** a remote command handler awaiting its token
- **WHEN** the invocation is cancelled
- **THEN** the child observes cancellation, the reply is `cancelled`, and no error notice is rendered

#### Scenario: A failing remote handler matches the in-process notice shape

- **GIVEN** a remote command handler that throws
- **WHEN** the invocation runs
- **THEN** the notice is the same shape as an in-process failure, names the source-qualified id, and the session continues

#### Scenario: A child without the capability is not offered its commands

- **GIVEN** a version-1 remote child that declares `ghost`
- **WHEN** the composition lists commands
- **THEN** `ghost` is absent from autocomplete and `/help`, and `/extensions` reports it as declared, not provided

#### Scenario: Unload during an invocation cancels it

- **GIVEN** a remote invocation in flight
- **WHEN** the extension is unloaded
- **THEN** the invocation's token is cancelled, a notice names the extension and command id, and the unload completes within its bound

### Requirement: Single-file extension discovery

The host SHALL treat every `*.cs` file directly under `.lunate/extensions/` (project) or `~/.lunate/extensions/` (user) as a single-file extension candidate, discovered beside packaged extensions, and SHALL report each one by its declared id. A file that declares no extension id SHALL be listed as `invalid (no #:extension)` and SHALL NOT be compiled. A file whose declared id collides with another candidate or with a packaged extension SHALL be refused with a message naming both paths.

#### Scenario: A file with a header appears in the extension list

- **GIVEN** `~/.lunate/extensions/acme.cs` whose first lines declare `#:extension acme-tools@1.0.0`
- **WHEN** the session composes
- **THEN** `/extensions` lists `acme-tools` as `not-built`, and no compiler runs

#### Scenario: A file without a header is not compiled

- **GIVEN** a `.cs` file in the extensions directory with no `#:extension` directive
- **WHEN** the session composes
- **THEN** it is listed as `invalid (no #:extension)` and nothing is compiled or restored

### Requirement: Header directives declare what the file provides

The header SHALL mirror the packaged manifest's declaration fields one to one — `#:tool`, `#:command`, `#:hook`, `#:service`, `#:capability` and `#:provider` for the manifest's `tools`, `commands`, `hooks`, `services`, `capabilities` and `modelProviders` — so a single file can never be unable to register something a package can. The header is the only declaration source for a single file; there is no sidecar manifest in this version. Directives SHALL be parsed before any compilation and their bytes SHALL be part of the trusted source digest. A registration the header does not declare SHALL refuse the load, naming the registration and the missing directive. A declared registration the file never makes SHALL be warned and shown by `/extensions` as `declared, not provided`.

#### Scenario: An undeclared tool registration is refused

- **GIVEN** a single-file extension whose header declares `#:tool fetch_issue` only
- **WHEN** the file also registers a tool named `close_issue`
- **THEN** the load is refused, naming `close_issue` and the missing `#:tool close_issue`

#### Scenario: Every manifest declaration kind has a directive

- **GIVEN** the packaged manifest's set of declaration fields
- **WHEN** the header grammar is inspected
- **THEN** each field maps to a directive, and a test fails if a manifest field is added without one

#### Scenario: A declared tool that is never registered is a warning

- **GIVEN** a header declaring `#:tool fetch_issue` and a body that registers nothing
- **WHEN** the extension loads
- **THEN** the extension is loaded, and `/extensions` shows `fetch_issue` as `declared, not provided`

### Requirement: Directive grammar and position

The host SHALL parse directives with its own scanner and SHALL NOT call SDK code to do it. The extension id SHALL follow the packaged manifest's rule exactly (`[a-z0-9-]+`), so a single file can be promoted to a package without renaming itself. A `#:tool` name SHALL match `[A-Za-z0-9_-]`, and the model-facing name `<id>__<tool>` SHALL fit the provider's 64-character limit; a pair that cannot fit SHALL be refused at discovery, naming the composed name and both halves. `#:package` SHALL accept exactly the documented .NET 10 file-based-app syntax — `#:package <id>@<version>`, where the version is a NuGet version or version range — and `#:extension` SHALL accept one id and one exact version. Only the leading block SHALL be scanned: directives, comments and blank lines before the first C# token. A `#:` line after code begins SHALL be an error naming its line number, never a silently ignored directive. `#:sdk`, `#:property` and `#:project` SHALL be refused with a message naming the directive. An unrecognised `#:` line inside the leading block SHALL be ignored with a warning. The accepted grammar SHALL be pinned by tests taken from the documented .NET 10 syntax.

#### Scenario: A version range is accepted

- **GIVEN** a header line `#:package Octokit@[14.0.0,15.0.0)`
- **WHEN** the header is parsed
- **THEN** the package is accepted with that range

#### Scenario: A directive after code is an error, not a silent no-op

- **GIVEN** a file whose line 12 is `#:tool late` and whose line 3 already contains a class declaration
- **WHEN** the header is parsed
- **THEN** the extension is refused with a message naming line 12

#### Scenario: A dotted id is refused like a package's would be

- **GIVEN** a header line `#:extension acme.tools@1.0.0`
- **WHEN** the header is parsed
- **THEN** the extension is refused, because a package would refuse the same id and a tool name may not contain a dot

#### Scenario: A tool name that cannot fit the provider limit is refused with both halves

- **GIVEN** a 40-character extension id and a 23-character tool name, whose composed name is 65 characters
- **WHEN** the header is parsed
- **THEN** the extension is refused, naming the composed name, the id and the tool name

#### Scenario: A refused directive is named

- **GIVEN** a header line `#:property LangVersion=preview`
- **WHEN** the header is parsed
- **THEN** the extension is refused with a message naming `#:property`

### Requirement: Trust before restore

A single-file extension SHALL be trusted before it is restored or compiled. The **first** trust SHALL cover the id, the declared packages as written (including version ranges) and the source bytes including the header; no lock exists yet. The first successful restore SHALL write the lock file and bind its **graph** into the trust record automatically — a restore downloads but executes nothing, so this needs no second prompt, provided compilation only ever runs against the bound lock. From then on the digest SHALL include the lock's graph half — the sources, and per package the exact version, SHA-512, resolved assets and dependency edges — and any change to that half SHALL make the extension `stale` and require a new decision. The lock's **toolset** half — target framework, RID, exact runtime, and the reference pack's version and nupkg hash — SHALL NOT be part of the digest: it SHALL be integrity-checked on restore, refused when it no longer matches its contents, included in the build cache key, and a change to it SHALL trigger a rebuild without a trust question. When the digest differs from the trusted one, the host SHALL stop before any feed request and ask again. A trust decision SHALL be per file, stored under `~/.lunate/trust.json`, and SHALL NOT be inherited from another file or path. The prompt SHALL name the file and its declared packages "and their dependencies", because the transitive set is not known before restore; after a restore `/extensions` SHALL show the resolved graph from the lock.

#### Scenario: First trust, restore, lock bound, compile

- **GIVEN** a new single-file extension declaring `#:package Octokit@14.0.0`
- **WHEN** the user trusts it and the first restore succeeds
- **THEN** the lock is written, its graph is bound into the trust record without a second prompt, and the compile runs against that lock

#### Scenario: A lock's graph edited by hand is stale and makes no feed request

- **GIVEN** a trusted, built extension whose `extension.lock.json` graph half is edited by hand
- **WHEN** the session composes with no network available
- **THEN** the extension is `stale`, the user is asked again, and the test observes zero HTTP requests to any feed

#### Scenario: A toolset change rebuilds without asking

- **GIVEN** a trusted, restored extension whose lock records runtime `10.0.13` and its reference pack
- **WHEN** it is restored on a host whose runtime is `10.0.14`, with the same source and the same resolution
- **THEN** the lock is rewritten for the new toolset and the resolution rebuilt, the trust digest is unchanged, and the trust prompt is never called

#### Scenario: A tampered toolset block is refused

- **GIVEN** a restored extension whose `extension.lock.json` toolset block is edited without its integrity hash being recomputed
- **WHEN** the extension is discovered and then restored
- **THEN** the lock is refused rather than used, the graph the user approved is still recognized as trusted, and the restore rewrites the block without asking

#### Scenario: A stale digest from a source edit makes no feed request

- **GIVEN** a trusted, built single-file extension
- **WHEN** one byte of its source changes and the session composes with no network available
- **THEN** the extension is `stale`, the user is asked again, and the test observes zero HTTP requests to any feed

### Requirement: Build-time code execution is opt-in

Compilation SHALL reference only the packages' compile-time assemblies; their analyzers and source generators SHALL NOT be loaded by default. A `#:generators on` header directive SHALL opt the file in, SHALL be part of the trusted digest, and only then SHALL the trust prompt state that build-time code from the named packages will run. `/extensions` SHALL show, for a package that ships generators while the file did not opt in, `generators skipped: <package>`, so the omission is visible rather than silent.

#### Scenario: Generators are skipped without the directive

- **GIVEN** a file declaring `#:package Refit@8.0.0`, a package that ships a source generator
- **WHEN** it is compiled without `#:generators on`
- **THEN** no generator runs, the compile succeeds on the reference assemblies alone, and `/extensions` shows `generators skipped: Refit`

#### Scenario: The opt-in changes the prompt

- **GIVEN** the same file with `#:generators on`
- **WHEN** the trust prompt is shown
- **THEN** it states that build-time code from the declared packages will run, and toggling the directive makes the digest stale

### Requirement: Reference assemblies for compilation

Compilation SHALL use the .NET reference assemblies matching the running runtime and the host SHALL NOT require an installed SDK to obtain them. When the feed publishes no pack at or above the runtime, the highest published pack of the same major and minor **below** it SHALL be used as a last resort, recorded in the lock with the rule `nearest-below-runtime`, because a patch release is API-compatible and compiling against a subset beats not compiling at all. The preference order SHALL be: the exact runtime version; otherwise the lowest published pack of the same major and minor at or above the runtime; otherwise the highest published pack of that major and minor below it. A runtime whose major and minor have no published pack at all SHALL fail, naming the runtime and the feeds tried. The reference pack (`Microsoft.NETCore.App.Ref`, the chosen version and RID) SHALL be restored through the same in-process NuGet engine into the global packages folder on first compile and reused from there, and the chosen version and rule SHALL be recorded in the lock's toolset half. A published self-contained single-file binary SHALL compile a single-file extension with an empty `HOME` and no `dotnet` on `PATH`; that run SHALL be a test.

#### Scenario: A self-contained binary compiles with no SDK and an empty home

- **GIVEN** the published `osx-arm64` single-file binary, `HOME` pointing at an empty directory, and no `dotnet` on `PATH`
- **WHEN** a single-file extension declaring one package is trusted and compiled
- **THEN** the reference pack is restored into the global packages folder, the compile succeeds, and a second run reuses the cache without a feed request

#### Scenario: A runtime ahead of the feed compiles against the highest pack below it

- **GIVEN** a host running runtime `10.0.14` and a feed that publishes `Microsoft.NETCore.App.Ref` up to `10.0.13`
- **WHEN** a single-file extension is restored and compiled
- **THEN** the pack `10.0.13` is used, the lock records the rule `nearest-below-runtime`, and the compile succeeds

#### Scenario: A pack at or above the runtime still wins over an older one

- **GIVEN** a host running runtime `10.0.14` and a feed publishing `10.0.13` and `10.0.15`
- **WHEN** the reference pack is chosen
- **THEN** `10.0.15` is chosen with the rule `nearest-at-least`

#### Scenario: No pack of the runtime's minor still fails and names the runtime

- **GIVEN** a host running runtime `10.0.5` and a feed that publishes only `9.0.4`
- **WHEN** a single-file extension is restored
- **THEN** the restore fails with `no Microsoft.NETCore.App.Ref is published for runtime 10.0.5`, naming the feeds it asked

### Requirement: Compiling a single file

The host SHALL compile a trusted single-file extension in process with the Roslyn it already ships, against the reference pack restored for the running runtime, the host's contract assemblies and each locked package's compile assets — and against nothing else. A compile SHALL NOT re-resolve a lock, SHALL NOT reach a feed, and SHALL NOT load a package's analyzers or source generators unless the header opted in. A file whose digest is `stale` SHALL NOT be compiled, and a lock that does not answer for the trusted bytes SHALL NOT be used. Compilation SHALL be deterministic: the same source, the same lock and the same toolset SHALL produce a byte-identical assembly, so a rebuild is a cache hit rather than a new artifact. A failed compile SHALL report the **first** diagnostic — the file, the line, the column, the compiler id and the message — as `failed (compile) — <reason>`, and SHALL leave no build directory behind. The build SHALL be triggerable without a session through `lunate extensions build <id|path> [--json]`, which runs the same service `/extensions reload` uses; listing extensions SHALL NOT compile anything, and startup SHALL stay lazy.

#### Scenario: A compile error leaves nothing behind

- **GIVEN** a trusted, restored extension whose source has a compile error
- **WHEN** `lunate extensions build <id>` runs
- **THEN** it exits non-zero, the build reports `failed (compile)` with the first diagnostic in Roslyn's own form (`path(line,col): error CSxxxx: message`), and no build directory exists for it

#### Scenario: The same inputs build once

- **GIVEN** an extension built once
- **WHEN** it is built again with the same source, lock and toolset
- **THEN** the build is a cache hit, no compiler runs, and the assembly's bytes are unchanged

#### Scenario: A package's copy of a contract is not compiled against

- **GIVEN** a locked package that ships its own `Lunate.Extensibility.Abstractions.dll`
- **WHEN** the extension is compiled
- **THEN** the host's contract assembly is the one referenced, and the compile succeeds rather than failing on two assemblies with one identity

### Requirement: Restore, lock and compiled cache

A restore SHALL run in process through the NuGet client libraries, which SHALL be loaded only when a restore runs. Resolved packages SHALL go to NuGet's global packages folder, respecting `NUGET_PACKAGES`, and SHALL NOT be copied into a Lunate-owned folder. Each extension SHALL keep only its compiled assembly, its `extension.lock.json` and a pointer to the current build under `~/.lunate/cache/extensions/<id>/`. The lock SHALL have two halves: a `graph` (the sources, and per package the exact version, nupkg SHA-512, the resolved asset list and the dependency edges) and a `toolset` (target framework, RID, exact runtime, and the reference pack's id, version and nupkg hash, with an integrity hash over the block). The build SHALL be keyed by `sha256(sourceHash | lockHash | apiVersion | TFM | RID)`, where `lockHash` covers both halves, so a graph change and a toolset change each produce a new build. A successful compile SHALL keep the last two builds and delete older ones; removing the extension SHALL delete its builds; `lunate cache prune` SHALL remove entries no extension points at. The builds SHALL live under `builds/<key>/`, each with the compiled assembly and a manifest that records its key's inputs and the resolution a load needs, and the entry SHALL carry a pointer file naming the current build. A restore that would change the lock's graph SHALL report and ask; a restore that would change only its toolset SHALL rewrite the lock and rebuild without asking. The state root SHALL be `~/.lunate` unless `LUNATE_HOME` names an absolute path, in which case the lock, the trust record, the build cache and the extension files SHALL all resolve under it; a relative value SHALL be refused by name rather than silently ignored, because a process that must not touch the user's state depends on the override taking effect.

#### Scenario: A second compile leaves two builds

- **GIVEN** an extension built once, then edited and built again, then edited and built a third time
- **WHEN** the third build succeeds
- **THEN** two build directories remain under its cache entry and the oldest is gone

#### Scenario: The state root can be moved

- **GIVEN** `LUNATE_HOME` set to an absolute temporary directory
- **WHEN** a single file is trusted, restored and compiled
- **THEN** its lock, its trust record and its build are all under that directory, and nothing is written under the user's profile

#### Scenario: No NuGet assembly before a restore

- **GIVEN** a session whose extensions are all already built
- **WHEN** the session starts
- **THEN** no `NuGet.*` assembly is loaded into the process

### Requirement: The state root is guarded, visible and environment-only

The state root SHALL be read from the process environment only — no `.env` file, settings file, `AGENTS.md` or project file may supply `LUNATE_HOME` — because a repository that can choose the store can choose the directory its own trust records live in. A process that knows its workspace or repository root SHALL refuse a `LUNATE_HOME` whose store resolves inside it, before opening the store, with the message `LUNATE_HOME points inside the workspace; trust records there could be written by the repository`. Containment SHALL compare real paths (symlinks resolved, `.` and `..` resolved, trailing separators ignored, the platform's case rule), a store equal to the root SHALL count as inside, and a sibling or unrelated directory SHALL be accepted. When `LUNATE_HOME` is set, `lunate --version` and the interactive welcome header SHALL both print the active home as `home <path> (LUNATE_HOME)`, and neither SHALL print it when the variable is unset. The trust store, the extension cache, user extensions, sessions, `auth.json`, `settings.json`, history, exports, the model catalog, Lunate's own user skills and recordings SHALL follow the root; `~/.agents/skills`, `~/.agents/mcp.json` and NuGet's packages folder SHALL NOT, because they are shared conventions or content-addressed package storage rather than Lunate state. No log file is promised: extension logging is the terminal through `IExtensionLog`, and the session file is the transcript.

#### Scenario: A store inside the workspace is refused

- **GIVEN** `LUNATE_HOME` set to a directory inside the worktree, or equal to its root
- **WHEN** a session composes, or `lunate extensions build`, `lunate cache prune` or `lunate trust` runs there
- **THEN** the command fails with `LUNATE_HOME points inside the workspace; trust records there could be written by the repository`, and nothing is written to that path

#### Scenario: A sibling or unrelated store is accepted

- **GIVEN** `LUNATE_HOME` set to a sibling of the worktree, or to a directory inside no workspace
- **WHEN** a session or a store-writing subcommand runs
- **THEN** it proceeds and writes under that directory

#### Scenario: A redirected run is visible

- **GIVEN** `LUNATE_HOME` set to an absolute temporary directory
- **WHEN** `lunate --version` runs and an interactive session starts
- **THEN** both print `home <that directory> (LUNATE_HOME)` in the same spelling, and a run without the variable prints neither

#### Scenario: A session with an unusable profile writes only the root

- **GIVEN** `LUNATE_HOME` set to a temporary directory, and `HOME`/`USERPROFILE` pointing at a directory that does not exist
- **WHEN** a full session runs one turn with a tool call
- **THEN** it exits successfully, its session file is under the redirected root, and the nonexistent home is still nonexistent

### Requirement: Host assemblies are the ones an extension loads

A built single-file extension SHALL load into its own collectible context, with every dependency resolved from the exact asset its build manifest records for a locked package — never re-resolved — and from the build directory otherwise. `Lunate.Extensibility.Abstractions`, `Lunate.Agent`, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` SHALL always resolve to the host's copies, including when a locked package ships its own and when a packaged extension's directory carries one: the copy SHALL be ignored, the load SHALL succeed, and exactly one line SHALL name what was dropped and who shipped it. That line SHALL be logged and SHALL appear on the extension's `/extensions` row (and as `ignoredCopy` in `--json`), because which copy is live is not something a user can see for themselves. The authoring template and the authoring page SHALL show the contract reference as `PrivateAssets="all"`, `ExcludeAssets="runtime"` and `Private="false"`, with one sentence saying the host owns that assembly.

#### Scenario: A copied Abstractions is ignored, not loaded twice

- **GIVEN** a locked package that ships `Lunate.Extensibility.Abstractions.dll`, and a second package that ships another copy
- **WHEN** the extension loads
- **THEN** the contract types in the loaded extension are the host's (the extension reports `typeof(IExtensionFactory).Assembly.Location` as the host's file, and its context holds no second Abstractions), the load succeeds, and exactly one line names both packages

#### Scenario: A packaged extension's own copy is ignored too

- **GIVEN** a packaged extension directory that contains a copy of the contract
- **WHEN** it loads
- **THEN** the host's copy is used and the row and the log name the directory once

#### Scenario: The template keeps the contract out of the output

- **GIVEN** the packaged-extension template
- **WHEN** its project file is read
- **THEN** the `Lunate.Extensibility.Abstractions` reference carries `PrivateAssets="all"`, `ExcludeAssets="runtime"` and `Private="false"`, and no copy of the DLL is written to the extension's output

### Requirement: The API gate is the version the build saw

An extension's compatibility SHALL be decided by the API version its build saw — the `apiVersion` a single-file build records in `build.json`, the `Lunate.Extensibility.Abstractions` assembly reference a packaged DLL carries, and the range a manifest declares — never by the presence of a DLL and never at the first call. A version above the host's `ExtensionApi.Current` SHALL refuse the load before any type of the extension is opened, with `requires Lunate ≥ x.y` naming both the required version and the running one; a version at or below it SHALL load, so a file compiled before an API bump keeps working. Discovery SHALL refuse a packaged manifest whose declared range the host cannot meet using the same wording when the range's floor is above the host. The contract assembly's own version SHALL equal `ExtensionApi.Current`, because a refusal that names a version no Lunate ships is worse than no refusal.

#### Scenario: At or below the host loads

- **GIVEN** a build recording `apiVersion` equal to the host's, and another recording an older one
- **WHEN** each loads
- **THEN** both load and neither reports `requires Lunate`

#### Scenario: Above the host is refused, naming both versions

- **GIVEN** a build recording `apiVersion` above the host's `ExtensionApi.Current`
- **WHEN** it loads
- **THEN** the load is refused with `requires Lunate ≥ <required> but this is Lunate <running>`, and no tool or command of the extension was registered

#### Scenario: A packaged DLL built against a newer contract is refused

- **GIVEN** a packaged extension whose assembly references `Lunate.Extensibility.Abstractions` at a version above the host's, while its manifest declares a compatible range
- **WHEN** it loads
- **THEN** the load is refused with `requires Lunate ≥ <that version>` before the factory is looked for, so a manifest cannot talk the host into opening code it cannot support

### Requirement: Extension states and reload

`/extensions` SHALL show each single-file extension in one of `not-built`, `restoring`, `built`, `stale`, `failed (trust|restore|compile)` or `invalid (no #:extension)`, with the reason on the same line for a failure. `/extensions reload <id>` — and the non-interactive `lunate extensions reload <id|path>`, which runs the same preflight, build and a register-only load — SHALL, in this order: preflight trust for the bytes on disk (asking the user, and stopping if they refuse); build against the lock bound to those bytes; and only then swap. A trust or build failure SHALL return with the old build still loaded, still callable, and still the build `builds.json` names. The swap SHALL cancel the old build's live invocations with a notice naming the extension, drain them within `ReloadDrainBound`, unload the old context, load the new build, and raise `ToolsChanged` exactly once after the new tool set is registered, so the list a model sees is never half-swapped.

#### Scenario: A reload that fails to compile keeps the old build

- **GIVEN** a loaded extension that is edited into a compile error
- **WHEN** `/extensions reload <id>` runs
- **THEN** the extension stays loaded at its previous build, its tool still answers, `builds.json` still names that build, and the reload reports the first diagnostic

#### Scenario: A refused trust question stops before the compiler

- **GIVEN** a loaded extension whose file was edited and whose trust question the user declines
- **WHEN** `/extensions reload <id>` runs
- **THEN** no compile runs, the old build stays loaded and callable, and the reload reports the refusal

#### Scenario: An untrusted file says what is missing

- **GIVEN** a single-file extension whose current bytes have no trust decision
- **WHEN** `/extensions` lists it
- **THEN** its row reads `not-built` and says it is not trusted yet and that `/extensions reload` asks before it builds, so a file that provides nothing reads as a decision rather than a defect

#### Scenario: A live invocation is drained, not cut

- **GIVEN** an extension command in flight
- **WHEN** `/extensions reload <id>` runs
- **THEN** the invocation is cancelled with a notice naming the extension, it leaves before the context is disposed, and its cancelled result is discarded

#### Scenario: A handler that ignores its token cannot hold the reload

- **GIVEN** a handler blocked on a manual reset event with its token unobserved
- **WHEN** `/extensions reload <id>` runs
- **THEN** the drain expires at `ReloadDrainBound`, exactly one log line names the command and says it did not return within the bound, and the reload completes anyway

#### Scenario: A command that runs during the build finishes on the old build

- **GIVEN** a command executing while a reload is still inside its build
- **WHEN** the build finishes and the swap runs
- **THEN** the command's result came from the old build and is kept, `ToolsChanged` fires once after the new build is loaded, and the next call answers from the new build

### Requirement: The model never approves trust

Trust SHALL be a user decision only. The model MAY write or edit a single-file extension and MAY ask for a reload, but no tool, command or protocol path SHALL let it record a trust decision, suppress a prompt, or load a changed file whose digest is stale. A model-initiated reload of a changed file SHALL end at the trust prompt and wait for the user.

#### Scenario: A model-initiated reload stops at the prompt

- **GIVEN** a loaded extension the model has just edited
- **WHEN** the model requests a reload through its tool
- **THEN** the reload stops at the trust prompt, the changed file is not compiled, and the old build stays loaded until the user answers

### Requirement: Built-in extension provenance

A normal manifest SHALL omit `builtin` (default `false`); a host-supplied built-in manifest SHALL
declare `builtin: true`. `builtin: true` SHALL mean the extension ships with the host: its assembly
is already in the default `AssemblyLoadContext`, its lifecycle and version are the host's, and it is
never installed, content-hash trusted, upgraded or uninstalled independently. A built-in SHALL be
supplied by the host composition (manifest plus an `IExtensionFactory`) and SHALL load through the
same `IExtensionContext`, registries, manifest cross-checks, background-services lifecycle and
teardown as a discovered extension. A host-supplied manifest without `builtin: true`, and a manifest
found by directory discovery that declares `builtin: true`, SHALL be refused with a message naming
the file and the reason. A discovered extension whose id equals a built-in id SHALL be skipped with one
discovery error naming both sources; a built-in SHALL never be shadowed. Built-ins SHALL load before
global and project descriptors, SHALL run in the default load context (no collectible ALC, no second
copy of a host assembly), and SHALL use the process-wide MSBuild bootstrap exactly once. A built-in
SHALL NOT be reloadable or unloadable in this version; session end stops its background services.
`--no-extensions` SHALL still compose the built-in set and its contributions, while skipping
discovery, trust, third-party loads, and third-party extension MCP servers, renderers and widgets,
so the model-facing behaviour the host ships stays available.

#### Scenario: A built-in loads through the shared activation path

- **GIVEN** a host-supplied built-in manifest and factory
- **WHEN** the session composes extensions
- **THEN** its factory runs against a normal extension context, its declared contributions enter the
  same registries, and its unload/teardown path is the one the loader uses for a discovered
  extension

#### Scenario: A directory cannot claim built-in provenance

- **GIVEN** a manifest under `~/.lunate/extensions/` or `.lunate/extensions/` with `builtin: true`
- **WHEN** discovery reads it
- **THEN** it is refused with a discovery error naming the path, and no built-in path is taken

#### Scenario: A host-supplied manifest must say it is built-in

- **GIVEN** a manifest supplied through the host's built-in list without `builtin: true`
- **WHEN** the composition loads built-ins
- **THEN** the composition refuses it with a message naming the manifest and the field

#### Scenario: A discovered extension cannot shadow a built-in

- **GIVEN** a built-in with id `roslyn` and a discovered extension with the same id
- **WHEN** discovery runs
- **THEN** the built-in is listed, the discovered descriptor is skipped, and one discovery error
  names both sources

#### Scenario: Built-ins run in the default context

- **GIVEN** a built-in whose assembly is already loaded by the host
- **WHEN** it loads
- **THEN** no collectible `AssemblyLoadContext` is created, no second Roslyn/MSBuild copy is loaded,
  and the process-wide MSBuild bootstrap is not registered twice

#### Scenario: --no-extensions keeps host built-ins

- **GIVEN** a session started with `--no-extensions`
- **WHEN** the tool set is composed
- **THEN** the built-in set is present and its contributions behave as without the flag, while no
  store is scanned and no third-party extension, extension MCP server, renderer or widget loads

### Requirement: Per-workspace extension services

`IExtensionContext` SHALL expose a registration-only workspace service facility. An extension SHALL
register a named service with a factory and declare it in the manifest `workspaceServices` list
(`name`, optional positive `maxLoadedWorkspaces`, default 2). A registered-but-undeclared service
SHALL refuse the load; a declared-but-unregistered service SHALL be logged and dropped. The
extension SHALL acquire the service for a canonical `WorktreeRoot` and receive a lease; the lease
SHALL pin the entry for its lifetime, and disposing it SHALL release the pin. Values SHALL be created
lazily on first acquisition per root, outside the registry lock, and disposed on eviction or
shutdown. The host SHALL cap loaded values per (extension, service) at the declared cap, evict the
least-recently-used unpinned entry, and SHALL NOT evict a pinned entry; when every loaded entry is
pinned, a new root's acquisition SHALL wait, cancellation-aware, rather than dispose under a live
call. On workspace switch the old root's entry SHALL remain loaded until it is the LRU candidate;
the same registry and cap SHALL apply to the session root and every child-run root. On shutdown the
registry SHALL release waiters, dispose every entry (pinned included), and fail later acquisitions
with `ObjectDisposedException`. Eviction SHALL dispose rebuildable resources only and SHALL NOT
delete durable data, sweep a shared store, or reset a session directory.

#### Scenario: A service is created lazily per workspace

- **GIVEN** an extension with a declared workspace service
- **WHEN** two runs in different worktrees acquire it for the first time
- **THEN** the factory runs once per root, each acquisition names its own root, and no value is
  created before the first acquisition

#### Scenario: A pinned entry is not evicted

- **GIVEN** the cap is held by a pinned entry
- **WHEN** another root acquires the service
- **THEN** the pinned entry stays loaded, the acquisition waits for a slot, and it proceeds when the
  lease is disposed or the caller cancels

#### Scenario: The LRU entry is evicted and disposed

- **GIVEN** the cap is held and at least one loaded entry is unpinned
- **WHEN** a new root acquires the service
- **THEN** the least-recently-used unpinned entry is removed and its value is disposed outside the
  registry lock, and the new root's value is created

#### Scenario: A workspace switch does not unload the old root

- **GIVEN** a loaded service for one worktree
- **WHEN** a child run in another worktree acquires the same service
- **THEN** the first entry stays loaded until it becomes the LRU candidate, and the shared cap
  applies across both roots

#### Scenario: Shutdown disposes every entry

- **GIVEN** a registry with a pinned and an unpinned entry
- **WHEN** the session shuts down
- **THEN** waiters are released, both entries are disposed, and a later acquisition fails with
  `ObjectDisposedException`

#### Scenario: Eviction never deletes durable data

- **GIVEN** a workspace service whose state includes files it did not create or a shared session
  store
- **WHEN** its entry is evicted
- **THEN** the value is disposed and no sweep, reference-count cleanup or directory reset runs

### Requirement: Conditional tool availability

A manifest SHALL accept an optional `toolActivation` object. A key SHALL be `*` (the default rule)
or a name declared in `tools`; any other key SHALL be a manifest error. A rule SHALL be a JSON object
with a `when` from the closed set `always`, `workspace-root-has-file` (with a non-empty `patterns`
array of direct-child file globs, no path separator) and `workspace-is-git-repository` (no extra
fields); an unknown condition or field SHALL be a manifest error. A declared tool with no applicable
rule SHALL always be available. The host SHALL evaluate the rule when it composes each run's tool
registry, against that run's workspace, and SHALL omit an inactive tool from the registry rather
than exposing it to the model, nested execution or programmatic stubs. The host SHALL evaluate only
at composition: a condition that changes while a run is in progress SHALL take effect at the next
composition (a child run, a reload that recomposes, or a new session), not by mutating the running
registry. An unreadable workspace SHALL evaluate a file condition as false and SHALL NOT fail the
session. `/extensions` SHALL report an inactive declared tool with the rule and the reason, and the
registered/declared counts SHALL be unchanged.

#### Scenario: The C# rule matches a solution at the root

- **GIVEN** a built-in manifest whose default rule is `workspace-root-has-file` with `*.sln`,
  `*.slnx` and `*.csproj`
- **WHEN** a run composes in a root containing one of those files
- **THEN** the tools are offered, and in a root with none of them they are absent from the
  declarations, nested calls and programmatic stubs

#### Scenario: The rule is evaluated by the host, not the extension

- **GIVEN** an extension with an activation rule and no code that scans the workspace
- **WHEN** a run composes in a matching and a non-matching root
- **THEN** the host decides both outcomes from the manifest rule and no extension code runs during
  prompt or tool-declaration building

#### Scenario: A mid-session change waits for the next composition

- **GIVEN** a running registry composed in a non-matching root
- **WHEN** a matching file appears at the root
- **THEN** the running registry is unchanged, and the next child run or new session in that root
  offers the tools

#### Scenario: Malformed rules are refused

- **GIVEN** a manifest with an unknown `when`, a path separator in a pattern, or a rule for an
  undeclared tool
- **WHEN** it is parsed
- **THEN** the error names the manifest, the tool key and the field, and the extension does not load

#### Scenario: The MCP server evaluates the rule through the same evaluator

- **GIVEN** `lunate mcp roslyn` running against a root, and the chat path's activation evaluator
- **WHEN** the server composes its tool list
- **THEN** it asks that evaluator rather than a copy of the rule, and a client's declared MCP `roots`
  are the roots evaluated; a client that declares no roots is answered for the server's working
  directory

#### Scenario: A root with no solution lists no cs_* tool

- **GIVEN** a root with no `.sln`, `.slnx` or `.csproj`
- **WHEN** `tools/list` is answered
- **THEN** it contains no `cs_*` tool, stderr carries exactly one line naming the rule and the
  patterns it looked for, and stdout carries nothing but protocol messages

#### Scenario: A new root announces tools/list_changed

- **GIVEN** a client that declared `listChanged` support, connected to a root that matched no rule
- **WHEN** the root gains a `.csproj` and the client sends `notifications/roots/list_changed`
- **THEN** the server re-evaluates, sends `notifications/tools/list_changed`, and a following
  `tools/list` contains the `cs_*` tools

#### Scenario: A client without listChanged keeps its snapshot

- **GIVEN** a client that did not declare `listChanged` support
- **WHEN** its roots change
- **THEN** the server sends no `tools/list_changed`, and the list the client received at
  `initialize` remains what it is served until it reconnects

#### Scenario: A solution removed without a roots event keeps the fix-it error

- **GIVEN** a listed `cs_*` tool whose root no longer holds the `.sln`/`.slnx` and no roots event was
  sent
- **WHEN** the tool is called
- **THEN** the call returns a tool error naming the missing file, and the tool stays listed because
  the composition it was built from has gone stale

### Requirement: Structured tool results

`ToolResultView` SHALL carry `StructuredContent` — a JSON object, JSON-serializable per ADR-0017
principle 3 — together with a `ContentKind` naming its schema (for example `lunate.diagnostics/v1`,
`lunate.diff/v1`, `lunate.command-result/v1`). A tool declaration SHALL be able to state an optional
output schema for its structured payload. The field names and shape SHALL mirror MCP's
`structuredContent` and `outputSchema`, so the MCP adapter maps structured results in both
directions. Built-in tools that already produce typed details SHALL project them into
`StructuredContent`; the internal `Details` handle SHALL NOT be published. The model SHALL continue
to receive `ResultText` exactly as before, and `StructuredContent` SHALL be the channel for
renderers, export, `--json` and MCP. The extension renderer surface SHALL receive
`StructuredContent` from the first stable release even if the renderer API itself stays minimal.

#### Scenario: A diagnostics result carries a typed payload

- **GIVEN** a `cs_diagnostics` call that returns two compiler errors
- **WHEN** the result is projected
- **THEN** `StructuredContent` holds the diagnostics and `ContentKind` is `lunate.diagnostics/v1`, and
  no consumer needs to parse `ResultText` to count or locate them

#### Scenario: An extension renderer renders from StructuredContent

- **GIVEN** an extension renderer for `lunate.diagnostics/v1`
- **WHEN** it is handed a `cs_diagnostics` result view
- **THEN** it dispatches on `ContentKind` and renders from `StructuredContent` without reading
  `ResultText`, and a result whose kind it does not know is rendered as text with a note rather than
  misrendered

#### Scenario: The model's prompt is unchanged

- **GIVEN** a tool result that carries `StructuredContent`
- **WHEN** the loop builds the next model request
- **THEN** the model receives the same `ResultText` it received before this contract, and the
  structured payload appears nowhere in the prompt

#### Scenario: MCP structured results map in both directions

- **GIVEN** a configured MCP server that returns `structuredContent`, and `lunate mcp roslyn` serving
  `cs_diagnostics`
- **WHEN** each result crosses the adapter
- **THEN** the former arrives as `StructuredContent` with a kind derived from its schema, and the
  latter is emitted as MCP `structuredContent` alongside its text content

### Requirement: Stable tool aliases

A manifest SHALL accept an optional `toolAliases` object mapping a declared tool's local name to a
model-facing alias. The canonical model-facing name SHALL remain `<extension-id>__<local-name>` for
identity, diagnostics and discovery; an alias SHALL be the name the model sees and SHALL be used for
declarations, nested calls, renderers and result views. An alias SHALL match `[A-Za-z0-9_-]{1,64}`,
SHALL NOT contain `__`, and SHALL NOT be a core tool name. Aliases SHALL be globally unique across
built-in, extension and canonical names; a collision SHALL refuse the later load with a message
naming both owners, except a collision with an MCP tool's model-facing name, which SHALL keep the
existing first-composition-wins rule (extension tools first, the MCP tool skipped with one
single-line diagnostic). An alias for an undeclared or unregistered tool SHALL be a manifest error.
A tool with no alias SHALL keep the canonical name. An alias SHALL be part of the extension's
published model-facing surface; changing or removing one is a breaking change that the host does not
rename for the extension.

#### Scenario: The flagship keeps cs_* without a rename

- **GIVEN** a built-in manifest declaring local names `cs_diagnostics`, `cs_find_symbol`,
  `cs_find_references`, `cs_outline` and `cs_rename` with aliases of the same values
- **WHEN** the session composes tools
- **THEN** the model sees exactly those five names, unchanged from today

#### Scenario: An extension can publish an alias

- **GIVEN** an extension whose local tool `diagnostics` declares the alias `cs_diagnostics`
- **WHEN** the tool is offered
- **THEN** the declaration carries `cs_diagnostics` while the extension keeps its own id and local
  name for diagnostics and grouping

#### Scenario: A collision refuses the later load

- **GIVEN** two extensions declaring the same alias, or an alias equal to a core tool name or
  another extension's canonical name
- **WHEN** the later extension loads
- **THEN** the load is refused with one message naming both owners

#### Scenario: An MCP collision keeps the existing rule

- **GIVEN** an extension alias and an MCP tool with the same model-facing name
- **WHEN** the session composes tools
- **THEN** the extension tool is registered, the MCP tool is skipped with one single-line
  diagnostic naming both sources, and the session continues

#### Scenario: An unknown alias target is refused

- **GIVEN** a `toolAliases` entry for a name not declared in `tools`
- **WHEN** the manifest is parsed
- **THEN** the error names the manifest, the tool and the field

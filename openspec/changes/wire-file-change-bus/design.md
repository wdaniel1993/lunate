# Design: wire-file-change-bus

## Context

The pieces exist and are spec'd; production composes none of them. Today: `ExtensionSession.StartAsync` registers only `core/workspace` (`ExtensionSession.cs:81`); `HarnessFactory.CreateTools` builds `WriteTool`/`EditTool` without a sink; the kit's `ExtensionTestHost` is the only place that creates a `FileChangeBus` and registers `core/file-bus`. `WriteTool.cs:113` / `EditTool.cs:244` already call `changes?.Notify(path)` after a successful mutation, and `FileChangeBus` already implements ordered per-workspace delivery, pattern filtering, handler timeouts and `DrainAsync`. `FileMutationQueue.Shared` is the tools' default queue; `MutationQueueHandle` wraps it for extensions.

## Decisions

### D1 — The session owns the bus

`ExtensionSession.StartAsync` creates `new FileChangeBus(workspace.WorktreeRoot, log)` **before the loader loads any extension** (so a subscriber registering during load is subscribed for the whole session), registers it as `core/file-bus` beside `core/workspace`, and exposes it as `ExtensionSession.FileChanges`. Workspace id = `WorktreeRoot` (the spec's rule). One bus per session; the kit keeps its own (test infra, unchanged).

### D2 — `core/mutation-queue` is registered as well

`RegisterCore("core/mutation-queue", new MutationQueueHandle(FileMutationQueue.Shared))`. Rationale: the queue is production-live — the core tools' default is the same `FileMutationQueue.Shared` — so the handle's promise ("applies atomically with the core's own file mutations") is true by construction, and the Service registry requirement's list of documented handles becomes fully real. Registering a per-session queue instead was rejected: it would diverge from the queue the tools actually use.

### D3 — The tools publish; `CreateTools` gains the sink

`HarnessFactory.CreateTools(workspace, files, changes)` passes `changes` into `WriteTool`/`EditTool` (ctor parameter already exists; notify call sites already exist). Frontends pass `extensions?.FileChanges`; with `--no-extensions` (or a failed composition) it is null and the tools behave exactly as today (no sink). No new producer sites: only the two tools mutate files, and both already notify.

### D4 — InteractiveSession's tool creation moves after extension composition

Today the interactive session builds `_tools` (`InteractiveSession.cs:240`) before `ComposeExtensionsAsync` (line 251). The tools must be built with the bus, so `_tools = HarnessFactory.CreateTools(_workspace, changes: _extensions?.FileChanges)` moves to right after `ComposeExtensionsAsync` and before `ComposeMcpToolsAsync` (which adds MCP tools to the same registry). Print and ACP modes already compose extensions before creating tools and just gain the argument.

### D5 — The harness options carry the sink

All three modes set `AgentHarnessOptions.FileChanges` from the session bus (default `NullFileChangeSink.Instance` otherwise). Note for reviewers: the property currently has **no consumer inside `Lunate.Agent`** — the tools are the producers — so this is coherence plus a correct value for any future consumer, not a behavior change.

### D6 — Lifecycle: drain, then dispose, at session end

`ExtensionSession.DisposeAsync`: `await _fileChanges.DrainAsync(CancellationToken.None)` (delivers everything enqueued so far to still-subscribed handlers; each dispatch is bounded by the handler timeout, so the drain cannot hang), then `Dispose()` (stops further enqueue), then the existing `EndSessionAsync` + MCP teardown. The start-cancellation path (`StartAsync`'s `OperationCanceledException` handler) disposes the bus as well. No drain at start; no drain on the interactive `/new`-style resets beyond the normal session disposal path.

### D7 — Spec deltas

- **File change bus** (MODIFIED): drop the trailing caveat sentence ("**Not yet wired in production** (tracked separately)…") — the rest of the requirement and all three scenarios stand verbatim, because the spec already described the production behavior.
- **Service registry** (MODIFIED): the registration sentence covers the three core handles — workspace identity, the file change bus, the mutation queue — registered when a session composes, production and kit alike, before any extension loads. Existing scenarios preserved verbatim; one scenario added: the handles are reachable **and live** in a composed session (a subscription through `core/file-bus` receives the session's events).

### D8 — No ADR, no public API change

The design is the spec's own intent; `FileChangeBus` and the handles are already public in `Lunate.Extensibility` (shipped), and everything new here is internal composition (`CreateTools` is internal, `ExtensionSession` is internal).

## Deviations

None yet.

## Test plan

- **Production e2e** (print mode, `Cli`-level seams store): a project extension subscribing through `core/file-bus` with a `*.cs` pattern; a `write` and an `edit` on matching files → exactly one event each, canonical path + worktree-root workspace id; a non-matching file → no event; a refused `edit` (no match) → no event. Runs in the `mcp-processes` collection if it spawns.
- **Session composition**: `ExtensionSessionTests` — `core/file-bus` and `core/mutation-queue` are now reachable (`FindCore` not-null), `core/workspace` unchanged; with `--no-extensions` no session is composed (existing test).
- **Mutation queue reachability**: an extension scheduling a read-modify-write through `core/mutation-queue` in a composed session → the mutation applies and serializes with a core write on the same path (order asserted).
- **Lifecycle**: a handler subscribed during load observes an event enqueued just before disposal (the drain proof); a disposed session's bus rejects further notifications without throwing.
- **Kit unchanged**: `FileChangeBusTests` / `FileChangeIntegrationTests` keep passing (no API change).

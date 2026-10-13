# Design: wire-file-change-bus

## Context

The pieces exist and are spec'd; production composes none of them. Today: `ExtensionSession.StartAsync` registers only `core/workspace` (`ExtensionSession.cs:81`); `HarnessFactory.CreateTools` builds `WriteTool`/`EditTool` without a sink; the kit's `ExtensionTestHost` is the only place that creates a `FileChangeBus` and registers `core/file-bus`. `WriteTool.cs:113` / `EditTool.cs:244` already notify after a successful mutation, and `FileChangeBus` already implements ordered per-workspace delivery, pattern filtering, handler timeouts and `DrainAsync`. `FileMutationQueue.Shared` is the tools' default queue; `MutationQueueHandle` wraps it for extensions. `AgentHarnessOptions.FileChanges` exists but nothing reads it; `ToolContext` already carries the harness-stamped `WorkingDirectory` and `RunId`.

## Decisions

### D1 — The session owns the bus

`ExtensionSession.StartAsync` creates `new FileChangeBus(workspace.WorktreeRoot, log)` **before the loader loads any extension** (so a subscriber registering during load is subscribed for the whole session), registers it as `core/file-bus` beside `core/workspace`, and exposes it as `ExtensionSession.FileChanges`. Workspace id = `WorktreeRoot` (the spec's rule). One bus per session; the kit keeps its own (test infra, unchanged).

### D2 — `core/mutation-queue` is registered, and its keys are normalized

- `RegisterCore("core/mutation-queue", new MutationQueueHandle(FileMutationQueue.Shared))` — the queue is production-live (the core tools' default is the same instance), so the handle's promise ("applies atomically with the core's own file mutations") is true by construction. A per-session queue was rejected: it would diverge from the queue the tools use.
- `FileMutationQueue` keys are normalized before locking: the full path with symlinks resolved, compared case-insensitively on Windows and macOS (their default filesystems are case-insensitive) and ordinally on Linux — so two spellings of one file share one lock. Under-normalizing would let two "locks" race on one file; over-normalizing (case-sensitive Linux unchanged) is safe. Tests cover case variants and a symlinked spelling.

### D3 — Tools publish through `ToolContext`, one path for every tool

Revised per sign-off: no sink parameter on `HarnessFactory.CreateTools`, no sink parameter on the tools' constructors.

- `IFileChangeSink.Notify(FileChangeEvent change)` (`Lunate.Agent`); `FileChangeEvent(AbsolutePath, RelativePath, WorkspaceId, RunId, Kind)` with `FileChangeKind { Created, Modified }`.
- The **harness** builds each run's `ToolContext.FileChanges` pre-bound to the workspace root and the run id (it knows both; session-level tool creation does not) — `ToolContext.FileChanges` defaults to `NullFileChangeSink.Instance`.
- The tools (`write`, `edit`) build the event from the values the harness stamped into their context and call the sink **inside the queued mutation, after the file write**: enqueueing while the queue's callback held means events preserve queue order exactly (the spec's ordering rule); delivery itself is asynchronous on the bus pump, so handlers never hold or wait on the queue path in a way that could deadlock, and the file content is complete before the event exists.
- "Same path for extension and MCP tools" means the one mechanism: every tool receives the same stamped `ToolContext.FileChanges` and can publish through it; the core only auto-publishes `write`/`edit` (D9's limits).
- The bus maps `FileChangeEvent` → `FileChangedPayload` (the contract assembly cannot be referenced by `Lunate.Agent`; the abstraction is the leaf) and matches subscriptions against the payload's absolute path (unchanged filter behavior).

### D4 — `AgentHarnessOptions.FileChanges` gets its consumer

The option stays and is now read: the harness forwards it into `ToolContext.FileChanges` per run, stamped with run id + workspace (D3). Subagents run their own harness (own worktrees, own run ids), so their events carry their own stamps. The three modes set the option from the session bus; `--no-extensions` (or a failed composition) leaves the default and the tools publish into nothing.

### D5 — Publish timing and the identical-content decision

- **After the write completes**, inside the queue callback (ordering-correct, see D3); a subscriber reading the file on delivery observes the new content — tested.
- **A write whose content is identical to the existing file still emits** (`modified`). Decided and spec'd: the write is a successful mutation (the file is rewritten; mtime changes; external watchers see it), suppressing it would need a content read (extra I/O, racy) and would hide "the tool wrote it" from subscribers, and subscribers are expected to treat notifications idempotently. Tested.

### D6 — Lifecycle: drain, drop-after-drain, dispose

`ExtensionSession.DisposeAsync`: `DrainAsync(CancellationToken.None)` (delivers what is enqueued; each dispatch bounded by the handler timeout), then dispose; notifications arriving **after the drain begins are dropped with one log line each** (so the drain can complete and slow handlers can never stall producers). Slow handlers never block tools: enqueue is a non-blocking channel write; interruption and timeouts live on the pump. The start-cancellation path disposes the bus too.

### D7 — The payload is enriched

`FileChangedPayload(Path, WorkspaceId)` (contract) becomes `(Path, RelativePath, WorkspaceId, RunId, Kind)`: canonical absolute path, workspace-relative path, workspace id (worktree root), run id (nullable — mutations outside a run), change kind (`created` / `modified`, serialized per the hook DTO conventions and covered by the JSON round-trip conformance test). The relative path is computed against the workspace root.

### D8 — The pattern filter is unchanged

Subscriptions keep matching the payload's absolute path with the existing `*`/`?` matcher (case-insensitive off Linux) — no behavior change; the richer payload adds fields, it does not alter matching.

### D9 — The spec's caveat is replaced by honest limits

The "not yet wired in production" sentence goes away; the requirement states the limits: file mutations made by the `bash` tool are **not** published (it writes as a side effect outside the tool path), and extension or MCP tools that write files publish through their `ToolContext.FileChanges` themselves (the same one mechanism). No `deleted` kind exists because no tool deletes files.

### D10 — The Roslyn subscriber is a follow-up card

Wiring the Roslyn backend as the first real subscriber (workspace sync) is filed as its own card rather than riding this change: it needs Roslyn-layer work and its own integration tests, while this change's e2e already proves the bus end-to-end through a real extension. The card names `core/file-bus` as its subscription path.

### D11 — Spec deltas

- **File change bus** (MODIFIED): caveat replaced by the D9 limits; the payload paragraph per D7; the identical-content rule per D5; the drain-drop rule per D6; the publish-completes-first rule per D5; existing scenarios preserved verbatim plus new scenarios (payload fields + kind, identical content, subscriber-reads-new-content, drain-drop).
- **Service registry** (MODIFIED): the registration sentence covers the three core handles, registered when a session composes — production and kit alike — before any extension loads; existing scenarios preserved; one added: the handles reachable **and live** in a composed session.

### D12 — No ADR; no reordering needed

The design is the spec's own intent. Note: with the ToolContext path (D3), no frontend needs to reorder tool creation — `HarnessFactory.CreateTools` is unchanged and every mode already builds its harness after extension composition. `FileChangeBus`, the handle and `ToolContext` are public (unshipped contract updates only); `ExtensionSession` stays internal.

## Deviations

- Apply: the payload's `WorkspaceId` maps from the event (harness stamp) with a fallback to the bus's own id — the old always-bus-id behavior was dropped, which also makes ACP per-session cwds stamp correctly. `DrainAsync` is a one-way latch by design.
- Review fixes (b7e8f39): `ResolveLinks` now `GetFullPath`-collapses relative symlink targets with parent segments before recursing (the first cut could yield two gates for one file via the extension path — falsified by a new test that fails without the fix); the bus logs drops after `Dispose` too and logs a completed-channel write; the e2e dropped its print-summary wording coupling; the contract round-trip covers the null-`RunId` wire form.
- Deferred: `ExtensionTestHost` one-run ergonomics (a second `RunAsync` silently drops file changes) — follow-up card.

## Test plan

- **Production e2e** (print mode, CLI-level seams store): a project extension subscribing through `core/file-bus` with a `*.cs` pattern; a `write` and an `edit` on matching files → exactly one event each, with absolute path, workspace-relative path, worktree-root workspace id, run id and kind (`created` / `modified`); a non-matching file → no event; a refused `edit` (no match) → no event.
- **ToolContext path**: an extension tool publishing via `ctx.FileChanges` yields one event with the correct run id and workspace (sign-off item).
- **Ordering + content**: concurrent queued mutations on one file → events in queue order; a subscriber reading the file on delivery sees the new content.
- **Identical content**: writing identical bytes still emits (`modified`).
- **Queue normalization**: case-variant spellings (macOS/Windows) and a symlinked spelling share one lock (serialized); Linux stays ordinal.
- **Session composition**: `ExtensionSessionTests` — `core/workspace`, `core/file-bus` and `core/mutation-queue` all reachable in a composed session (the T-45 sign-off composition test, completed here).
- **Mutation queue reachability**: an extension scheduling a read-modify-write through `core/mutation-queue` applies and serializes with a core write on the same path.
- **Lifecycle**: a handler subscribed during load observes an event enqueued just before disposal (drain proof); after the drain starts, notifications are dropped and logged; slow handlers never stall a producer.
- Kit tests updated for the event-shaped bus API (`FileChangeBusTests`, `FileChangeIntegrationTests`, `ExtensionLoaderFileChangeTests`); process-spawning tests join the `mcp-processes` collection.

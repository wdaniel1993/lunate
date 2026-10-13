# Proposal: wire-file-change-bus

## Why

The extensions spec's "File change bus" requirement — one `FileChanged` notification per successful `write`/`edit` mutation, pattern-filtered subscriptions, ordered delivery — carries its own caveat: **"Not yet wired in production"**. Only the test kit (`ExtensionTestHost`) registers `core/file-bus`; a production session composes no bus, builds its tools without a sink, and never registers the handle. A real extension that subscribes through `core/file-bus` gets the loader's "no core/file-bus service is registered" warning and receives nothing, so the T-39 criterion ("file bus events after write/edit") holds only in tests. The `core/mutation-queue` handle — documented alongside the bus in the Service registry requirement — is likewise unreachable, although the queue itself is live in production (the tools' default is `FileMutationQueue.Shared`).

Card `t_ea4a1b32` (filed during the T-45 recon) closes the gap: production sessions wire the bus and both handles exactly as the spec already describes, and the caveat goes away.

## What

- **The session owns the bus**: `ExtensionSession.StartAsync` creates one `FileChangeBus` (workspace id = worktree root, the session's log) before any extension loads, registers it as `core/file-bus` next to `core/workspace`, and exposes it for the frontends.
- **`core/mutation-queue` is registered too**: `MutationQueueHandle` over the same queue the core tools use (`FileMutationQueue.Shared`) — and the queue's keys are normalized (symlinks resolved; case-insensitive on Windows/macOS) so two spellings of one file share one lock.
- **Tools publish through `ToolContext`** — one path for core, extension and MCP tools: no sink parameter on `CreateTools` or the tools' constructors. `IFileChangeSink.Notify(FileChangeEvent)` with `(AbsolutePath, RelativePath, WorkspaceId, RunId, Kind)`; the harness stamps run id and workspace into each run's `ToolContext.FileChanges` (it knows both; session-level tool creation does not — and subagents stamp their own). Events are published inside the queued mutation after the file write, so ordering matches the queue and a subscriber reading the file sees the new content.
- **`AgentHarnessOptions.FileChanges` gains its consumer**: the harness forwards it into `ToolContext`, making the option live instead of decorative.
- **The payload is enriched**: `FileChangedPayload` carries absolute path, workspace-relative path, workspace id, run id and change kind (`created`/`modified`) — contract + conformance round-trip updated; subscriptions keep matching the absolute path with the existing filter.
- **Decided and spec'd**: a write with identical content still emits (`modified`); after a drain begins, notifications are dropped with a log line; slow handlers can never stall tools; `bash`-made file changes are not published (honest limit — extension/MCP tools that write publish through their own context).
- **Lifecycle**: drain → dispose at session end (bounded by handler timeouts); start-cancellation path disposes too.
- **Spec deltas**: the "File change bus" caveat sentence is replaced by those limits plus the enriched payload/kind rules; the "Service registry" registration sentence covers all three handles and gains a scenario proving they are reachable **and live**.
- **Follow-up card** (filed): wire the Roslyn backend as the first real subscriber (workspace sync) — its own change, since it needs Roslyn-layer work and integration tests.

## Impact

- `src/Lunate.Agent/`: `IFileChangeSink` + `FileChangeEvent` + `FileChangeKind`; `ToolContext.FileChanges`; the harness stamps it per run; `FileMutationQueue` key normalization. (`PublicAPI.Unshipped` updates.)
- `src/Lunate.Extensibility.Abstractions/`: `FileChangedPayload` enriched + conformance test.
- `src/Lunate.Extensibility/`: `FileChangeBus` consumes events, maps to payloads, drains with drop-and-log; `ExtensionLoaderFileChangeTests`/`FileChangeBusTests` updated.
- `src/Lunate.Coding/`: `ExtensionSession` (bus creation, registration, exposure, disposal); `HarnessFactory.CreateTools` unchanged; `WriteTool`/`EditTool` publish via context (ctor sink parameter dropped); the three modes set `AgentHarnessOptions.FileChanges`.
- `tests/Lunate.Coding.Tests/`: production-path e2e (one event per successful mutation with full payload, none for refused/non-matching), ToolContext path test, ordering + read-sees-content, identical-content, session composition (all three handles), mutation-queue reachability, lifecycle drain/drop.
- Specs: extensions (two requirements).
- No new packages, no ADR (the design is the spec's own intent).

## Out of scope

- The Roslyn backend subscription (follow-up card; `core/file-bus` named as its path).
- Publishing `bash`-made file changes (no reliable boundary — honest limit in the spec).
- A `deleted` kind (no tool deletes) and HTTP/remote hosts (T-52).


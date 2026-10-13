# Proposal: wire-file-change-bus

## Why

The extensions spec's "File change bus" requirement — one `FileChanged` notification per successful `write`/`edit` mutation, pattern-filtered subscriptions, ordered delivery — carries its own caveat: **"Not yet wired in production"**. Only the test kit (`ExtensionTestHost`) registers `core/file-bus`; a production session composes no bus, builds its tools without a sink, and never registers the handle. A real extension that subscribes through `core/file-bus` gets the loader's "no core/file-bus service is registered" warning and receives nothing, so the T-39 criterion ("file bus events after write/edit") holds only in tests. The `core/mutation-queue` handle — documented alongside the bus in the Service registry requirement — is likewise unreachable, although the queue itself is live in production (the tools' default is `FileMutationQueue.Shared`).

Card `t_ea4a1b32` (filed during the T-45 recon) closes the gap: production sessions wire the bus and both handles exactly as the spec already describes, and the caveat goes away.

## What

- **The session owns the bus**: `ExtensionSession.StartAsync` creates one `FileChangeBus` (workspace id = worktree root, the session's log) before any extension loads, registers it as `core/file-bus` next to `core/workspace`, and exposes it for the frontends.
- **`core/mutation-queue` is registered too**: `MutationQueueHandle` over the same queue the core tools use (`FileMutationQueue.Shared`), so handle-scheduled mutations serialize with core writes — the documented read-only handles become fully reachable.
- **The tools publish**: `HarnessFactory.CreateTools` gains the sink and passes it to `WriteTool`/`EditTool` (their `Notify` call sites already exist and are already tested in the kit). The three modes (interactive, print, ACP) pass the session bus; the interactive session's tool creation moves after extension composition so the bus exists when the tools are built.
- **The harness options carry it**: `AgentHarnessOptions.FileChanges` is set from the session bus in all three modes (the property currently has no consumer inside the harness — the tools are the producers — and is set for coherence and future consumers).
- **Lifecycle**: session disposal drains the bus (pending events reach still-subscribed handlers; per-handler timeouts bound the drain) and disposes it, before the session-ending hooks and MCP teardown; the start-cancellation path disposes it too.
- **Spec deltas**: the "File change bus" requirement drops the "not yet wired in production" sentence (everything else and all scenarios stay); the "Service registry" requirement's registration sentence covers all three core handles and gains a scenario proving the handles are reachable and live in a composed session.

## Impact

- `src/Lunate.Coding/`: `ExtensionSession` (bus creation, registration, exposure, disposal), `HarnessFactory.CreateTools` (sink parameter), `InteractiveSession` (tool-creation order + sink), `PrintMode`, `AcpMode` (sink in harness options + tools).
- `tests/Lunate.Coding.Tests/`: production-path e2e (print session, project extension subscribing `*.cs`, one event per successful mutation, none for a refused mutation), the session-composition test updates (`core/file-bus` / `core/mutation-queue` become reachable), a mutation-queue reachability test.
- Specs: extensions (two requirements).
- No new packages, no ADR (the design is the spec's own intent; no public API change — `FileChangeBus`, the handles and `CreateTools` are internal/kit-internal where new).

## Out of scope

- Background services beyond the bus/queue wiring (the rest of T-39 is already landed).
- An `AgentHarness`-internal consumer of `FileChanges` (nothing mutates files inside the harness today).
- HTTP/remote extension hosts (T-52) and the remaining fitness samples.

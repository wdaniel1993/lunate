# Tasks: wire-file-change-bus

## 1. The session owns the bus

- [ ] 1.1 `ExtensionSession.StartAsync`: create the per-session `FileChangeBus` (workspace id = `WorktreeRoot`, the session's log) before any extension loads; register it as `core/file-bus` beside `core/workspace`; expose it as `ExtensionSession.FileChanges`.
- [ ] 1.2 Register `core/mutation-queue` as `MutationQueueHandle(FileMutationQueue.Shared)`.
- [ ] 1.3 Dispose the bus on the start-cancellation path; on `DisposeAsync`: drain (bounded by handler timeouts) → dispose → existing session-ending hooks + MCP teardown.
- [ ] 1.4 Session-composition tests: `core/file-bus` and `core/mutation-queue` reachable in a composed session; `core/workspace` unchanged; `--no-extensions` composes nothing.

## 2. The tools publish

- [ ] 2.1 `HarnessFactory.CreateTools` gains the sink parameter and passes it to `WriteTool`/`EditTool`.
- [ ] 2.2 InteractiveSession: build `_tools` after `ComposeExtensionsAsync` (before `ComposeMcpToolsAsync`) with `changes: _extensions?.FileChanges`.
- [ ] 2.3 PrintMode and AcpMode: pass the session bus to `CreateTools` and set `AgentHarnessOptions.FileChanges` from it (all three modes; note the option currently has no in-harness consumer).

## 3. Production-path end-to-end

- [ ] 3.1 Print-mode e2e: project extension subscribing `core/file-bus` with a `*.cs` pattern — `write` and `edit` on matching files emit exactly one event each (canonical path, worktree-root workspace id); a non-matching file emits none; a refused `edit` emits none.
- [ ] 3.2 Mutation-queue reachability: an extension schedules a read-modify-write through `core/mutation-queue` in a composed session — it applies and serializes with a core write on the same path (order asserted).
- [ ] 3.3 Lifecycle: an event enqueued just before disposal still reaches a handler subscribed during load (drain proof); a disposed bus rejects further notifications without throwing.
- [ ] 3.4 New process-spawning tests join the `mcp-processes` collection.

## 4. Gate

- [ ] 4.1 `scripts/verify.sh` green (both cultures), `openspec validate --all --strict` clean, csharpier + markdownlint clean.
- [ ] 4.2 Spec deltas stay in sync at archive (extensions: file change bus caveat dropped; service registry sentence + live-handles scenario).

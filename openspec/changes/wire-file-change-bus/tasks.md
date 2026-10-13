# Tasks: wire-file-change-bus

## 1. The event contract and the bus

- [x] 1.1 `Lunate.Agent`: `FileChangeKind { Created, Modified }`, `FileChangeEvent(AbsolutePath, RelativePath, WorkspaceId, RunId, Kind)`, `IFileChangeSink.Notify(FileChangeEvent)` (NullFileChangeSink no-ops); PublicAPI.Unshipped updated.
- [x] 1.2 `FileChangedPayload` gains `RelativePath`, `RunId`, `Kind` (contract assembly; serialization per the hook DTO conventions; conformance round-trip test updated).
- [x] 1.3 `FileChangeBus` consumes events (IFileChangeSink), maps them to payloads for handlers, keeps ordered delivery/pattern filtering (absolute path)/handler timeouts; drop-and-log once a drain has begun; `Dispose` unchanged otherwise.
- [x] 1.4 Kit tests updated for the event-shaped API (`FileChangeBusTests`, `FileChangeIntegrationTests`, `ExtensionLoaderFileChangeTests`); new: drain-drop logged; notifications after drain do not throw.

## 2. The mutation queue

- [x] 2.1 `FileMutationQueue`: normalize keys before locking — full path, symlinks resolved, case-insensitive comparison on Windows and macOS, ordinal on Linux — so two spellings share one lock.
- [x] 2.2 Tests: case-variant spellings serialize as one lock (macOS/Windows); a symlinked spelling shares the lock; Linux stays ordinal; queued order still per key.

## 3. Session composition

- [x] 3.1 `ExtensionSession.StartAsync`: create the per-session `FileChangeBus` (workspace id = `WorktreeRoot`, the session's log) before any extension loads; register `core/file-bus` and `core/mutation-queue` (`MutationQueueHandle(FileMutationQueue.Shared)`) beside `core/workspace`; expose `ExtensionSession.FileChanges`.
- [x] 3.2 Dispose on the start-cancellation path; on `DisposeAsync`: drain (bounded by handler timeouts) → dispose → existing session-ending hooks + MCP teardown.
- [x] 3.3 Session-composition tests: `core/workspace`, `core/file-bus` and `core/mutation-queue` all reachable (the T-45 sign-off composition test); `--no-extensions` composes nothing.

## 4. The ToolContext path

- [x] 4.1 `ToolContext.FileChanges` (IFileChangeSink, default NullFileChangeSink); the harness forwards `AgentHarnessOptions.FileChanges` into it per run, stamped with workspace root + run id.
- [x] 4.2 `WriteTool`/`EditTool`: drop the ctor sink parameter; build the event from the harness-stamped context values and publish **inside the queued mutation, after the file write** — kind `created` for a new file, `modified` otherwise; a write with identical content still emits (`modified`).
- [x] 4.3 The three modes set `AgentHarnessOptions.FileChanges` from the session bus (InteractiveSession, PrintMode, AcpMode); `CreateTools` unchanged.
- [x] 4.4 Test: an extension tool publishing via `ctx.FileChanges` yields one event with the correct run id and workspace.

## 5. Production-path end-to-end

- [x] 5.1 Print-mode e2e: project extension subscribing `core/file-bus` with a `*.cs` pattern — `write` and `edit` on matching files emit exactly one event each (absolute path, workspace-relative path, worktree-root workspace id, run id, kind); a non-matching file emits none; a refused `edit` emits none.
- [x] 5.2 Ordering + content: concurrent queued mutations on one file → events in the same order as the mutations applied; a subscriber reading the file on delivery sees the new content.
- [x] 5.3 Mutation-queue reachability: an extension schedules a read-modify-write through `core/mutation-queue` in a composed session — it applies and serializes with a core write on the same path.
- [x] 5.4 Lifecycle: an event enqueued just before disposal still reaches a handler subscribed during load (drain proof); a slow handler never stalls a producer (enqueue returns immediately).
- [x] 5.5 New process-spawning tests join the `mcp-processes` collection. (No new test in this change spawns a process.)

## 6. Gate

- [ ] 6.1 `scripts/verify.sh` green (both cultures), `openspec validate --all --strict` clean, csharpier + markdownlint clean.
- [ ] 6.2 Spec deltas stay in sync at archive (extensions: file change bus — caveat replaced by the limits + payload/kind/identical-content/drain rules; service registry sentence + live-handles scenario).

# Design: sample-lsp-diagnostics

## Context

T-45: a reference extension attaching LSP diagnostics via `ToolResultReady`. Facts the design leans on (all verified against the tree):

- `ToolResultReadyPayload` carries `RunId`, `CallId`, `ToolName`, `Output`, `IsError` — **no arguments** — and `ToolResultReadyResult(Output, Data)` composes in order; the harness maps `Data` to `Details`, which is **UI-only, never sent to the model** (`ToolResult.cs` doc; `AgentHarness.Tools.cs:382`).
- `ToolResult` has `Output` (model), `IsError`, `Details` (UI-only), **`StructuredContent`** (typed, next to the model-facing text) and `Usage`. `StructuredContent` is produced by the MCP adapter today; `ToolResultReady` cannot contribute it.
- The execution site has the approved arguments in hand (`approvedArgs`, `AgentHarness.Tools.cs:304-322`), so threading them into the payload is a small, honest primitive.
- `FileChangeBus` exists and `ExtensionTestHost` registers `core/file-bus`, but **production never wires it**: `HarnessFactory.CreateTools` builds `WriteTool`/`EditTool` without a sink and nothing in `Lunate.Coding` creates a bus. `FileChanged` delivery is also asynchronous (queued pump), so it is the wrong trigger for attaching to the *same* tool result. Out of scope; filed as its own card (`t_ea4a1b32`); the spec now marks it "not yet wired in production".
- The Service-registry requirement promises core handles "(file bus subscription, mutation queue scheduling, workspace identity)" — but **nothing is registered in production**; only the kit registers `core/file-bus`. The sample needs the workspace identity, so this change registers `core/workspace`.
- `WorkspaceInfo(WorktreeRoot, RepoRoot, GitCommonDir)` is the designed identity record; `IExtensionContext.TryGetCoreService<T>(name, out service)` is the lookup.
- The hook runner enforces a per-handler timeout (`HookRunnerOptions.HandlerTimeout`, default **5 s**; provider fast path 100 ms) per ADR-0017; handler failures follow the catalogue policy "report, keep original".

## Decisions

### D1 — Trigger: `ToolResultReady` on `write`/`edit`, path from the call's arguments

The handler acts when `ToolName` is `write` or `edit` and the call's arguments carry a `path` ending in `.cs`. It resolves the path against `core/workspace`'s worktree root (canonicalized, must land inside the workspace). **No output-text parsing.** Non-matching tools, non-C# files, missing `path`, or missing workspace identity → the result passes through untouched. This is why the payload gains `Arguments` (as approved — after any `ToolCalling` mutation, the handler sees what actually ran).

### D2 — Attach shape: labeled text (model) + structured content (model), details untouched

- **Text** appended to the output, labeled with the source: `lsp-diagnostics ({serverCommand}): {N} diagnostics for {path}` + one line per diagnostic (`severity line:character message (code)`, 1-based positions; LSP's 0-based positions convert once, at the boundary). Errors and warnings only (LSP severity 1–2); capped at `maxDiagnostics` (setting, default 5) with a `+n more diagnostics` line.
- **Structured** copy on `ToolResultReadyResult.StructuredContent`: `{"path", "diagnostics":[{severity,line,character,code,message}]}` — model-facing typed data, per the field contract.
- **Details** stays untouched (UI-only; the sample adds no renderer).
- Zero diagnostics (after filtering): no attach, one info log.

### D3 — Version-matched diagnostics

The client keeps one document per file: first request `textDocument/didOpen` (version 1), later requests `textDocument/didChange` (version N, full text). It accepts only `textDocument/publishDiagnostics` notifications whose `version` equals the version it sent — a stale notification is ignored and the wait continues until the timeout. The fake server sends a stale version first in the dedicated test.

### D4 — A minimal stdio LSP client, one process per session

`LspClient` speaks JSON-RPC over stdio with `Content-Length` framing: `initialize` → wait result → `initialized` → didOpen/didChange → bounded wait for matching `publishDiagnostics`. One request in flight (a lock); one shared process, lazily started on the first eligible result, owned by a registered `IBackgroundService` so it stops idempotently at session end (shutdown → exit → kill fallback → dispose). A dead process is restarted on next use; a persistent failure is logged (the log is the notice — no notice API exists) and the result passes through unchanged. The whole round trip is bounded by the `timeoutMs` setting (default 2000) — strictly under the runner's 5 s per-handler timeout, so a slow server degrades gracefully inside the handler instead of being cancelled by the runner.

### D5 — Failure policy: keep the original, never break the run

The handler catches everything (server missing, protocol error, timeout, file read failure), logs through the extension log, and returns the original result. On runner-level cancellation (the 5 s per-handler timeout), the existing "report, keep original" policy applies — no partial attachment.

### D6 — The fake server is a real process

`FakeLspServer` is a tiny console project the tests reference, so its dll lands in the test output; tests set `serverCommand = "dotnet"`, `serverArgs = ["<test-output>/FakeLspServer.dll", ...]` (cross-platform without apphost naming games). Modes: default (deterministic diagnostics from content — a `TODO` on a line yields a warning with code `todo-check`; supports sending a stale version first), `--silent` (accepts didOpen/didChange, never publishes — the timeout test), `--marker <path>` (writes the path on start — proves process start and one-start-per-session), `--crash-after <n>` (exits mid-session — the restart test).

### D7 — `core/workspace` registration

`ExtensionSession.StartAsync` receives the session's workspace identity and registers `core/workspace` on the loader's service registry **before any extension loads**; `ExtensionTestHost` registers it too (parity with `core/file-bus`). It is a per-composition value, never a static or global singleton — so per-run worktree workspaces can register their own identity later. It gets its **own test, independent of the sample** (a composed session resolves the handle and it matches the session's workspace). The plumbing (widened parameter vs. a small context record) is the implementation's choice; the behavior is what the spec pins. A composition test additionally pins the set of core services registered in production composition (with the not-yet-wired set named in the test, so wiring `core/file-bus`/`core/mutation-queue` flips it).

### D8 — Tests drive the real stack

Kit-based (`ExtensionTestHost`), the real loader, the real harness, the real `write`/`edit` tools, the real hook runner, a real fake-server process. The model client is a small local scripted `IChatClient` that records every request (deterministic; no digest coupling — the composed output feeds the next request, so fixture digests would be circular). Assertions: the composed tool-result text and `StructuredContent` on the result, **the exact next request containing the diagnostics text** (captured by the scripted client), the extension log lines, and the marker file for process lifecycle.

## Alternatives considered (devil's advocate)

- **Parse the path from output text**: rejected (maintainer steer) — the call's arguments are the source of truth; the missing payload field is the right fix, and it serves every future handler, not just this one.
- **Trigger on `FileChanged`**: rejected — delivery is asynchronous (ordered queue) and unwired in production; attaching to the same call's result would race. Right trigger for notifications later, not for result attachment.
- **Query the LSP for every tool result** (not just `write`/`edit`): rejected — noisy, starts the server for unrelated calls, and muddies the story.
- **Structured data in `Details`**: rejected — `Details` is UI-only and never reaches the model; diagnostics are actionable for the model, so they go to `StructuredContent` (with the text as the primary carrier).
- **In-process fake** (a scripted LSP client double instead of a process): rejected — the fitness point is the real stdio protocol; the fake must be a real process to prove process ownership, framing and timeouts.
- **Replay fixtures** instead of a scripted client: rejected — the second exchange's request digest depends on the composed output (the very thing under test); scripted client is deterministic and precedent exists in `Lunate.Coding.Tests`.
- **Ship a real LSP client as a core feature**: rejected — guide says explicitly not v1 scope; the sample is the proof that extensions can build it.
- **Register all three core handles now**: rejected — `core/mutation-queue` has no consumer and the file bus needs its own wiring change with lifecycle and sink plumbing; one change per session.

## Risks

- **Arguments shape**: `path` is assumed (the `write`/`edit` argument contract, ADR-0012 tool contract); a call without a usable `path` passes through — tested.
- **Server startup cost in tests**: the fake is a `dotnet` process (~0.1–0.2 s); tests reuse one host per scenario and the default timeout covers slow CI.
- **Windows paths and URIs**: the URI conversion is a pure function tested with Windows input (`C:\src\Foo.cs` → `file:///C:/src/Foo.cs`) plus separator-agnostic resolution (a lesson already learned twice).
- **Concurrency**: hooks for tool results are sequential per run, but the client still serializes with a lock; a timeout must not wedge later calls (the process is killed and restarted on the next use if it fails to respond).

## Deviations

*(the apply records deviations here)*

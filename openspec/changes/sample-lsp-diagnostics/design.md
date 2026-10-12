# Design: sample-lsp-diagnostics

## Context

T-45: a reference extension attaching LSP diagnostics via `ToolResultReady`. Facts the design leans on:

- `ToolResultReadyPayload` carries `RunId`, `CallId`, `ToolName`, `Output`, `IsError` — no args, no workspace. `ToolResultReadyResult(Output, Data)` composes in order; the harness replaces the output and attaches `Data` as the result's details (`AgentHarness.Tools.cs:382`).
- `WriteTool`'s success output is `wrote {lines} lines to {relative} ({created|replaced})`; `EditTool`'s is `edited {relative} lines {first}–{last} (match: {tier})` — both carry the workspace-**relative** path, deterministically parseable.
- `FileChangeBus` exists and `ExtensionTestHost` registers `core/file-bus`, but **production never wires it**: `HarnessFactory.CreateTools` builds `WriteTool`/`EditTool` without a sink and nothing in `Lunate.Coding` creates a bus. `FileChanged` delivery is also asynchronous (queued pump), so it is the wrong trigger for attaching to the *same* tool result. Out of scope here; filed as its own card.
- The Service-registry requirement says core services SHALL be exposed as documented read-only handles "(file bus subscription, mutation queue scheduling, workspace identity)" — but **nothing is registered in production**; only the kit registers `core/file-bus`. The sample needs the workspace identity, so this change registers `core/workspace`; the rest goes to the filed card.
- `WorkspaceInfo(WorktreeRoot, RepoRoot, GitCommonDir)` is the designed identity record; `IExtensionContext.TryGetCoreService<T>(name, out service)` is the lookup (`core/…` names are reserved and not shadowable).

## Decisions

### D1 — Trigger: `ToolResultReady` on `write`/`edit`, path parsed from the output

The handler acts when `ToolName` is `write` or `edit` and the output contains a `.cs` token; it resolves the relative path against `core/workspace`'s worktree root (canonicalized) and must land inside the workspace. Non-matching tools, non-C# files and missing workspace identity pass the result through untouched. No output parsing beyond the two documented formats plus a `.cs` token scan fallback.

### D2 — Attach shape: output text + structured details

Diagnostics are appended to the output as a readable section (`lsp-diagnostics: N diagnostics for {path}` + one line per diagnostic: `severity line:character message (code)`), and the same data goes into `ToolResultReadyResult.Data` as `{"path", "diagnostics":[{severity,line,character,code,message}]}` with 1-based positions (LSP positions are 0-based; the conversion happens once, at the boundary). Zero diagnostics: no attach, one info log. The message text is model-facing: it says what is wrong and where.

### D3 — A minimal stdio LSP client, one process per session

`LspClient` speaks JSON-RPC over stdio with `Content-Length` framing: `initialize` → wait result → `initialized` → `textDocument/didOpen` (languageId `csharp`, full text) → bounded wait for `textDocument/publishDiagnostics` for that URI → (no didClose; the file stays open for later edits). One request in flight (a lock), one shared process, lazily started on the first eligible result, owned by a registered `IBackgroundService` so it stops idempotently at session end (shutdown → exit → kill fallback → dispose). Timeout: `timeoutMs` setting (default 2000) bounds the whole round trip; on timeout the result is returned unchanged and the reason is logged.

### D4 — Failure policy: keep the original, never break the run

The handler catches everything (server missing, protocol error, timeout, file read failure), logs through `IExtensionLog` and returns the original output. A failing diagnostics fetch must never change or break a tool result.

### D5 — The fake server is a real process

`FakeLspServer` is a tiny console project the tests reference, so its dll lands in the test output; tests set `serverCommand = "dotnet"`, `serverArgs = ["<test-output>/FakeLspServer.dll", ...]` (cross-platform without apphost naming games). Modes: default (deterministic diagnostics from content — a `TODO` on a line yields a warning with code `todo-check`), `--silent` (accepts `didOpen`, never publishes — the timeout test), `--marker <path>` (writes the path on start — proves process start and one-start-per-session).

### D6 — `core/workspace` registration

`ExtensionSession.StartAsync` receives the session's workspace identity and registers `core/workspace` on the loader's service registry before any extension loads; `ExtensionTestHost` registers it too (parity with `core/file-bus`). The plumbing (widened parameter vs. a small context record) is the implementation's choice; the behavior is what the spec pins.

### D7 — Tests drive the real stack

Kit-based (`ExtensionTestHost`), the real loader, the real harness, the real `write`/`edit` tools, the real hook runner, a real fake-server process. The model client is a small local scripted `IChatClient` (deterministic; no digest coupling — the composed output feeds the next request, so fixture digests would be circular). Assertions: the composed tool-result text and details as recorded in the session, the extension log lines, and the marker file for process lifecycle.

## Alternatives considered (devil's advocate)

- **Trigger on `FileChanged`**: rejected — delivery is asynchronous (ordered queue) and unwired in production; attaching to the same call's result would race. It is the right trigger for *notifications* later, not for result attachment.
- **Query the LSP for every tool result** (not just `write`/`edit`): rejected — noisy, starts the server for unrelated calls, and muddies the story.
- **In-process fake** (a scripted LSP client double instead of a process): rejected — the fitness point is the real stdio protocol; the fake must be a real process to prove process ownership, framing and timeouts.
- **Replay fixtures** instead of a scripted client: rejected — the second exchange's request digest depends on the composed output (the very thing under test); scripted client is deterministic and precedent exists in `Lunate.Coding.Tests`.
- **Ship a real LSP client as a core feature**: rejected — guide says explicitly not v1 scope; the sample is the proof that extensions can build it.
- **Register all three core handles now**: rejected — `core/mutation-queue` has no consumer and the file bus needs its own wiring change with lifecycle and sink plumbing; one change per session.

## Risks

- **Path parsing fragility**: the two documented output formats are stable and tested; the fallback scan keeps unknown formats harmless (no match → pass-through).
- **Server startup cost in tests**: the fake is a `dotnet` process (~0.1–0.2 s); tests reuse one host per scenario and the default timeout covers slow CI.
- **Windows paths**: relative paths use platform separators; the URI conversion and the `.cs` token scan must be separator-agnostic (a lesson already learned twice).
- **Concurrency**: hooks for tool results are sequential per run, but the client still serializes with a lock; a timeout must not wedge later calls (the process is killed and restarted on the next use if it fails to respond).

## Deviations

*(the apply records deviations here)*

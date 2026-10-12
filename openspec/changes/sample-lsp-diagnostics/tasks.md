# Tasks: sample-lsp-diagnostics

## 1. Core primitives (payload arguments + structured content)

- [x] 1.1 `ToolResultReadyPayload` / `AgentToolResultReadyContext` gain `Arguments` (JsonElement, the call's arguments as approved after `ToolCalling`); thread `approvedArgs` at the execution site (`AgentHarness.Tools.cs:304-322`) through `AgentHookRecords`/the adapter to the DTO
- [x] 1.2 `ToolResultReadyResult` / `AgentToolResultReadyResult` gain `StructuredContent` (JsonElement?); the harness maps `StructuredContent = ready.StructuredContent ?? result.StructuredContent` (Details mapping unchanged)
- [x] 1.3 DTO conformance round-trip tests updated for both fields; harness-level test: a handler returning structured content sees it on the tool result

## 2. Core: workspace identity registration

- [x] 2.1 Register `core/workspace` (the session's `WorkspaceInfo`) at composition in `ExtensionSession.StartAsync` (production) and `ExtensionTestHost` (kit parity), before any extension loads; plumb the identity from the Coding layer's workspace; per-composition value, no static
- [x] 2.2 Own tests (independent of the sample): the handle is reachable in a composed session and matches the session's workspace; lookup before registration fails soft (no throw)
- [x] 2.3 Composition test pinning the core services registered in production composition (not-yet-wired set named in the test: `core/file-bus`, `core/mutation-queue`)

## 3. The LSP client (sample)

- [x] 3.1 `LspClient`: stdio JSON-RPC with `Content-Length` framing; `initialize` → `initialized` → `didOpen` (v1) / `didChange` (vN, full text) → bounded wait for `publishDiagnostics` **with the matching version** (stale versions ignored, wait continues); one request in flight (lock)
- [x] 3.2 `LspServerService` (`IBackgroundService`): lazy start on first use, idempotent stop (shutdown → exit → kill fallback → dispose); settings `serverCommand` (default `csharp-ls`), `serverArgs`, `timeoutMs` (default 2000, under the runner's 5 s per-handler timeout), `maxDiagnostics` (default 5); a dead process is restarted on next use
- [x] 3.3 URI conversion as a pure, unit-tested function: Windows drive paths (`C:\src\Foo.cs` → `file:///C:/src/Foo.cs`) and POSIX paths; separator-agnostic resolution

## 4. The handler (sample)

- [x] 4.1 `LspDiagnosticsToolResultReadyHandler`: acts on `write`/`edit` when the call's arguments carry a `.cs` `path`; resolves via `core/workspace`; pass-through when not applicable (other tools, non-C#, no path, no identity)
- [x] 4.2 Attach: labeled text section (source label, errors+warnings only, cap + `+n more`, 1-based positions) + `StructuredContent`; zero → no attach + info log; every failure → original result + log (keep-original policy)
- [x] 4.3 Manifest (`hooks: ["tool-result-ready"]`, settings schema), factory, README (capability table like the other samples)

## 5. Fake LSP server

- [x] 5.1 `FakeLspServer` console project (referenced by the tests so its dll lands in the test output): initialize capabilities, didOpen/didChange → deterministic diagnostics (`TODO` line → warning `todo-check`), stale-version-first mode, `--silent`, `--marker <path>`, `--crash-after <n>`, shutdown/exit handling
- [x] 5.2 Tests set `serverCommand = "dotnet"` + `serverArgs` with the fake's dll path (cross-platform)

## 6. Tests (the done-when)

- [x] 6.1 `write` a `.cs` with a `TODO` → the tool result text carries the labeled diagnostics section, the result carries `StructuredContent`, and **the exact next model request contains the diagnostics text** (scripted client captures requests); the marker proves the fake started once
- [x] 6.2 `edit` a `.cs` → same attachment path (didChange vN)
- [x] 6.3 Non-C# `write` → output untouched, fake never started (no marker, no log)
- [x] 6.4 Stale version first → only the matching version's diagnostics are attached
- [x] 6.5 `--silent` fake → timeout → original output unchanged + logged; a later call still works (restart)
- [x] 6.6 `--crash-after` → the next call restarts the server; persistent failure is logged and the result passes through
- [x] 6.7 Server starts once across mutations and stops once at session end (marker + log + no lingering process)
- [x] 6.8 Cap + filter: >`maxDiagnostics` errors/warnings → capped with `+n more`; info/hint severities excluded

## 7. Gate + record

- [x] 7.1 Both sample projects join `lunate.sln`; `bash scripts/verify.sh` green (the gate picks the new test project up); deviations recorded in design.md
- [ ] 7.2 Archive, PR, CI green

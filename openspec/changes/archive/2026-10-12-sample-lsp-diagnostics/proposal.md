# Proposal: sample-lsp-diagnostics

## Why

Guide card T-45 (phase 6): the extensibility architecture is proven by a fitness suite of reference extensions in CI, and `lsp-diagnostics` is the LSP story — a sample that attaches language-server diagnostics to tool results right after a change. A sample that needs a workaround means the core is missing a primitive; this one surfaced two: the `ToolResultReady` payload does not carry the call's arguments, and its result cannot contribute model-facing structured content. Both are added here, minimally, per the contract's own intent. It also needs workspace identity, which is designed (`core/workspace`) but never registered in production.

## What

- **The sample** (`samples/extensions/lsp-diagnostics/`): an `IToolResultReadyHandler` that, after a successful `write`/`edit` on a `.cs` file, asks a language server for that file's diagnostics and attaches them to the tool result. The path comes from the call's arguments (the new payload field), never parsed from output text; the file is resolved through the workspace identity handle.
- **A minimal LSP client** in the sample: stdio JSON-RPC with `Content-Length` framing (`initialize` → `initialized` → `textDocument/didOpen`/`didChange` → version-matched `textDocument/publishDiagnostics`), one server process per session owned by a background service (lazy start, stopped at session end, restarted after a crash). Settings: `serverCommand` (default `csharp-ls`), `serverArgs`, `timeoutMs`, `maxDiagnostics`.
- **Diagnostics shaping**: errors and warnings only, capped (`maxDiagnostics`, default 5) with a `+n more` line, labeled with the source; a readable section appended to the output (model-facing) plus structured content on the result (model-facing typed data) — details stay untouched (UI-only).
- **Two core primitives**: `ToolResultReadyPayload` gains the call's arguments (as approved, after any `ToolCalling` mutation); `ToolResultReadyResult` gains `StructuredContent`, mapped by the harness.
- **A fake LSP server** (`FakeLspServer`, a tiny console project referenced by the tests): deterministic diagnostics derived from file content (`TODO` lines become warnings; sends a stale document version first; a `--silent` mode hangs, for the timeout test; a `--marker <path>` mode proves process start), so CI stays hermetic.
- **Core addition**: register `core/workspace` (the session's workspace identity) wherever the host composes — production sessions and the test kit — before any extension loads. This closes the gap between the Service-registry requirement and the CLI.
- **Tests**: attach after `write` and `edit`; the exact next model request contains the diagnostics text; no attach and no server start for non-C# files; stale-version diagnostics are ignored; timeout → the result is kept unchanged and logged; crash → restarted, persistent failure logged; the server starts once and stops once; the structured content is set on the result; workspace identity has its own test independent of the sample; a composition test pins the core services registered in production composition.

## Impact

- `samples/extensions/lsp-diagnostics/`: extension, fake server, tests, README; both projects join `lunate.sln` (the gate picks the tests up automatically).
- `src/Lunate.Agent/AgentHarness.Tools.cs` + `AgentHookRecords.cs` and `src/Lunate.Extensibility/HookRunner.Tools.cs` + the DTOs: the two payload/result additions and their mappings.
- `src/Lunate.Coding/ExtensionSession.cs` (+ the test kit): `core/workspace` registration at composition.
- Specs: extensions (Hook wiring, File change bus, Service registry); `docs/spec/extensibility.md` catalogue and fitness rows.

## Out of scope

- The file-change bus in production (`core/file-bus` + `core/mutation-queue` wiring) — discovered during recon as unwired outside the test kit; filed as its own card; the spec now says so explicitly.
- Shipping a generic LSP extension for other languages (guide: explicitly not v1 scope); the sample demonstrates the pattern with a fake server.

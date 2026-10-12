# Proposal: sample-lsp-diagnostics

## Why

Guide card T-45 (phase 6): the extensibility architecture is proven by a fitness suite of reference extensions in CI, and `lsp-diagnostics` is the LSP story — a sample that attaches language-server diagnostics to tool results right after a change. A sample that needs a workaround means the core is missing a primitive; this one needs workspace identity, which is designed (`core/workspace`) but never registered in production.

## What

- **The sample** (`samples/extensions/lsp-diagnostics/`): an `IToolResultReadyHandler` that, after a successful `write`/`edit` on a `.cs` file, asks a language server for that file's diagnostics and attaches them to the tool result — text appended to the output, structured data attached as the result's details. The tool output carries the file as a workspace-relative path; the handler resolves it through the workspace identity handle.
- **A minimal LSP client** in the sample: stdio JSON-RPC with `Content-Length` framing (`initialize` → `initialized` → `textDocument/didOpen` → bounded wait for `textDocument/publishDiagnostics`), one server process per session owned by a background service (lazy start, stopped at session end). Settings: `serverCommand` (default `csharp-ls`), `serverArgs`, `timeoutMs`.
- **A fake LSP server** (`FakeLspServer`, a tiny console project referenced by the tests): deterministic diagnostics derived from file content (`TODO` lines become warnings; a `--silent` mode hangs, for the timeout test; a `--marker <path>` mode proves process start), so CI stays hermetic.
- **Core addition**: register `core/workspace` (the session's workspace identity) wherever the host composes — production sessions and the test kit — before any extension loads. This closes the gap between the Service-registry requirement and the CLI.
- **Tests**: attach after `write` and `edit`; no attach and no server start for non-C# files; timeout → the result is kept unchanged and logged; the server starts once and stops once; the structured details carry the diagnostics.

## Impact

- `samples/extensions/lsp-diagnostics/`: extension, fake server, tests, README; both projects join `lunate.sln` (the gate picks the tests up automatically).
- `src/Lunate.Coding/ExtensionSession.cs` (+ the test kit): `core/workspace` registration at composition.
- Specs: extensions (Service registry — the registration is pinned).

## Out of scope

- The file-change bus in production (`core/file-bus` + `core/mutation-queue` wiring) — discovered during recon as unwired outside the test kit; filed as its own card.
- Shipping a generic LSP extension for other languages (guide: explicitly not v1 scope); the sample demonstrates the pattern with a fake server.

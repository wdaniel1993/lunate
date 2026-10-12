# Tasks: sample-lsp-diagnostics

## 1. Core: workspace identity registration

- [ ] 1.1 Register `core/workspace` (the session's `WorkspaceInfo`) at composition in `ExtensionSession.StartAsync` (production) and `ExtensionTestHost` (kit parity), before any extension loads; plumb the identity from the Coding layer's workspace
- [ ] 1.2 Tests: the handle is reachable in a composed session and carries the worktree root; lookup before registration still fails soft (no throw)

## 2. The LSP client (sample)

- [ ] 2.1 `LspClient`: stdio JSON-RPC with `Content-Length` framing; `initialize` → `initialized` → `textDocument/didOpen` → bounded wait for `textDocument/publishDiagnostics`; one request in flight (lock); separator-agnostic URI conversion
- [ ] 2.2 `LspServerService` (`IBackgroundService`): lazy start on first use, idempotent stop (shutdown → exit → kill fallback → dispose); settings `serverCommand` (default `csharp-ls`), `serverArgs`, `timeoutMs` (default 2000); a dead process is restarted on next use

## 3. The handler (sample)

- [ ] 3.1 `LspDiagnosticsToolResultReadyHandler`: acts on `write`/`edit` results with a `.cs` path (the two documented formats + token-scan fallback); resolves via `core/workspace`; pass-through when not applicable
- [ ] 3.2 Attach: readable output section + structured `Data` (`path`, `diagnostics[{severity,line,character,code,message}]`, 1-based); zero diagnostics → no attach + info log; every failure → original output + log (keep-original policy)
- [ ] 3.3 Manifest (`hooks: ["tool-result-ready"]`, settings schema), factory, README (capability table like the other samples)

## 4. Fake LSP server

- [ ] 4.1 `FakeLspServer` console project (referenced by the tests so its dll lands in the test output): initialize capabilities, didOpen → deterministic diagnostics (`TODO` line → warning `todo-check`), `--silent` mode, `--marker <path>` mode, shutdown/exit handling
- [ ] 4.2 Tests set `serverCommand = "dotnet"` + `serverArgs` with the fake's dll path (cross-platform)

## 5. Tests (the done-when)

- [ ] 5.1 `write` a `.cs` with a `TODO` → the tool result text carries the diagnostics section and the details carry the structured data (asserted from the session); the marker proves the fake started once
- [ ] 5.2 `edit` a `.cs` → same attachment path
- [ ] 5.3 Non-C# `write` → output untouched, fake never started (no marker, no log)
- [ ] 5.4 `--silent` fake → timeout → original output unchanged + logged; a later call still works (restart)
- [ ] 5.5 Server starts once across mutations and stops once at session end (marker + log + no lingering process)
- [ ] 5.6 Scripted model client (local, deterministic); no fixtures needed

## 6. Gate + record

- [ ] 6.1 Both sample projects join `lunate.sln`; `bash scripts/verify.sh` green (the gate picks the new test project up); deviations recorded in design.md
- [ ] 6.2 Archive, PR, CI green

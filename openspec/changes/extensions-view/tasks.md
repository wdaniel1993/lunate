# Tasks — extensions-view

## 1. Draft

- [x] 1.1 Proposal + design + tasks + spec deltas; `openspec validate --all --strict`; draft commit.

## 2. Apply

- [x] 2.1 `ExtensionSession`: `ExtensionLoadResult` + `ExtensionLoadState`, per-descriptor timing at compose (D1).
- [x] 2.2 Register-only inspect mode: `ExtensionSeams.Inspect` → `Load(startSession: false)` skips session-start hooks, background-service start and the `ToolsChanged` hook; unload releases contexts (D3).
- [x] 2.3 Registered-count accessors: `HookRunner` and `ServiceRegistry` per-extension counts (tools via `ExtensionToolRegistry.List()`; commands registered are always 0) (D5).
- [x] 2.4 `ExtensionsView` formatter: order (failed, trust-denied, loaded, then id), segments, `(N declared)` mismatch notes, `(slow)` above 500 ms, first-sentence truncation + ` — see the log`, empty/disabled texts; JSON shape (D2/D5).
- [x] 2.5 `/extensions` interactive command (dispatch case + notice) per D2.
- [x] 2.6 `lunate extensions [--json]` subcommand + `Cli.cs` routing: `(CLI-measured)` labels, exit 1 on any failed, `--no-extensions` handling (D3).
- [x] 2.7 Tests: session E2E (`/extensions` states/order/empty/disabled), CLI text/JSON/empty/register-only marker test/exit code, formatter unit tests (order, truncation, slow, mismatch); `Cli.cs` help text.
- [x] 2.8 Touched suites green (Coding; Extensibility if touched).

## 3. Gate

- [ ] 3.1 `scripts/verify.sh` green (both cultures).

## 4. Review

- [ ] 4.1 Adversarial review; fix findings; focused re-review.

## 5. Close

- [ ] 5.1 Archive (`openspec archive extensions-view --yes`); tick tasks.
- [ ] 5.2 PR + CI green + closing comment.

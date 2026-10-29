# Sample: lsp-diagnostics

An extension that attaches language-server diagnostics to the tool result right after a successful
`write` or `edit` of a C# file. The path comes from the call's arguments (never parsed from output
text) and resolves through the `core/workspace` identity. The extension owns one language server
process per session, started lazily on the first eligible result and stopped at session end.

## Core capabilities proven

| Capability | Where |
| --- | --- |
| `ToolResultReady` with the call's arguments | `LspDiagnosticsToolResultReadyHandler` acts when `ToolName` is `write`/`edit` and `payload.Arguments` carries a `.cs` `path`; the payload's arguments are the call's as approved, so a `ToolCalling` handler's mutation is what the handler sees. |
| Model-facing structured content | The same diagnostics are attached as typed `StructuredContent` (`{"path","diagnostics":[…]}`); the harness carries it on the tool result. UI-only details stay untouched. |
| `core/workspace` resolution | The handler resolves relative paths against the workspace identity registered by the host and refuses paths that leave the worktree. |
| Background service lifecycle | `LspServerService` is registered as `ext/lsp-diagnostics/server`: lazy start, restart after a dead process, idempotent stop (shutdown → exit → kill fallback → dispose). |
| Bounded hooks | The whole round trip is bounded by `timeoutMs` (default 2000, strictly under the runner's 5 s per-handler timeout); every failure keeps the original result and is logged. |
| Version-matched documents | `didOpen` (v1) and `didChange` (vN, full text) per file; only the `publishDiagnostics` notification with the version just sent is accepted, stale versions are ignored. |
| Testing kit against a real process | `LspDiagnosticsExtension.Tests` drives the real loader, harness, `write`/`edit` tools and hook runner against `FakeLspServer`, a real console process speaking stdio JSON-RPC. |

## Layout

```text
samples/extensions/lsp-diagnostics/
  LspDiagnosticsExtension/              the extension: manifest, factory, handler, LSP client
  LspDiagnosticsExtension.Tests/        kit-based tests, the scripted model client and the probe
  LspDiagnosticsExtension.Tests/FakeLspServer/  the deterministic fake language server
  README.md
```

## Behaviour

- **Applicability** — `write`/`edit` calls whose `path` ends in `.cs` and resolves inside the
  worktree. Everything else (other tools, non-C# paths, a missing `path`, a missing workspace
  identity) passes through untouched and silent.
- **Attachment** — a labeled text section appended to the output:

  ```text
  lsp-diagnostics (csharp-ls): 2 diagnostics for src/Foo.cs
  error 5:1 FIXME left on line 5 (fixme-check)
  warning 9:1 TODO left on line 9 (todo-check)
  ```

  Errors and warnings only (LSP severity 1–2), capped at `maxDiagnostics` with a
  `+n more diagnostics` line, positions converted from LSP's 0-based units once. The same capped
  list is attached as `StructuredContent`; `Details` is never touched. No errors or warnings after
  filtering → nothing attached, one info log.
- **Failure policy** — keep the original result: a server that is missing, crashes, hangs or
  answers garbage is logged and the next eligible call starts a fresh process.
- **Process ownership** — the process starts with the first eligible result, is keyed to the
  session, and stops at `SessionEnding`; a crash mid-round-trip is restarted on the next call.

## Settings

`serverCommand` (string, default `csharp-ls`), `serverArgs` (array), `timeoutMs` (number, default
2000 — the whole round trip budget), `maxDiagnostics` (number, default 5). The fake server tests
point `serverCommand` at `dotnet` with the fake's dll in `serverArgs`.

## The fake server

`FakeLspServer` derives diagnostics from the document text (first match per line wins): a line
with `FIXME` yields an error, `TODO` a warning, `INFO` an information and `HINT` a hint. Modes:
`--silent` never publishes, `--marker <path>` appends the PID on start, `--crash-after <n>` exits
after n messages, `--stale-first` publishes a stale version before the matching one. A second
`didOpen` for an open document answers with a protocol-error diagnostic, so the `didChange` path is
observable.

## Copy and extend

1. Copy this directory, rename the projects and namespaces, and update `extension.json`
   (`id`, `entryAssembly`, `apiVersion`, `hooks`, `services`, `settingsSchema`).
2. Keep the tests: they install the built extension into a temp directory, point it at
   `FakeLspServer` and drive the real stack; the recorded model client asserts the diagnostics text
   lands in the exact next model request.
3. Swap `LspServerService` for your own server command and language id; the handler's shaping,
   resolution and failure policy carry over unchanged.

See `templates/extension/README.md` for the full manifest and hook wiring reference.

# Sample: code-mode

An extension that contributes one tool, `codemode__run`, which executes model-written JavaScript in
a **child process** (Node.js by default) and bridges every script-side tool call back through
`ToolContext.ExecuteToolAsync`. The script never talks to the core directly: it gets a generated
JavaScript prelude whose `tools` object carries this run's programmatically callable tools — the
core's typed stubs rendered with JSDoc — and each `await tools["name"](args)` crosses a framed
JSON-lines protocol into the full pipeline (validation, hooks, approval, cancellation, nested ids
`"<parent>/<n>"`). The annotations are not read-only and destructive-capable, so the approval policy
asks before a run.

## What it proves (T-48)

| Capability | Where |
| --- | --- |
| Typed stubs | `ToolStubs.Describe` (core) feeds `CodeModeStubs`, which renders the JSDoc prelude from `ToolContext.ToolStubs`. |
| The stamped callable set | The harness stamps every run's `Direct`/`Programmatic`/`Deferred` tools; the script calls a `Programmatic` fixture that is never declared to the model. |
| The bridge pipeline | `CodeModeBridge` answers `call` frames with `ToolContext.ExecuteToolAsync`: nested events and session records carry the canonical `"<parent>/<n>"` ids, and a declined destructive nested call is a catchable script error. |
| Approval isolation | An "always" grant on `codemode__run` covers that tool only; a bridged destructive tool still asks. |
| The process boundary | `core/process` runs the child with a deadline, a scrubbed environment allow-list, an output cap and a tree kill on timeout or cancel (`CodeModeToolHandler`). |
| Structured result | `StructuredContent` carries `exitCode`, `timedOut`, the script's `result`, the captured output, `stderr` and the bridged-call list. |
| Protocol ownership | The embedded runtime footer emits the `hello` handshake first and turns `console.*` and `process.stdout.write` into `log` frames, so script output can never be mistaken for protocol. |

## Layout

```text
samples/extensions/code-mode/
  CodeModeExtension/            the extension: manifest, factory, settings, handler, bridge,
                                JSDoc renderer and the embedded runtime footer
  CodeModeExtension.Tests/      kit-based tests with a real node child, scripted model client
  README.md
```

## Settings

- `runtime` (string, optional): the script runtime executable; default `"node"`.
- `timeoutSeconds` (integer 1–600, optional): the script deadline; default `30`. A call's own
  `timeoutSeconds` may only tighten it.
- `maxOutputBytes` (integer ≥ 1, optional): the captured-output cap; default `65536`. Beyond it the
  logs are truncated with a marker (the runner keeps draining so the child never blocks).
- `maxToolCalls` (integer ≥ 1, optional): the bridged tool-call cap per script; default `100`.
  Beyond it the bridge answers with a teaching error frame the script can catch.

## Build and test

```sh
dotnet build samples/extensions/code-mode/CodeModeExtension/CodeModeExtension.csproj -c Release
dotnet test samples/extensions/code-mode/CodeModeExtension.Tests/CodeModeExtension.Tests.csproj
```

The tests install the built extension into a temp store through `Lunate.Extensibility.Testing`'s
`ExtensionTestHost` (real manifest, real load context, real harness) and drive it with a scripted
model client — no live APIs; Node.js must be on `PATH` (the suite already requires it).

## Install

Copy the extension's output directory (`CodeModeExtension.dll`, `extension.json`, its dependencies)
under `<worktree>/.lunate/extensions/codemode/` and trust it on first load, exactly like the other
samples. Nothing runs until the model calls `codemode__run`; the call asks for approval. A settings
file at `~/.lunate/extensions-settings/codemode.json` overrides the defaults, for example
`{ "timeoutSeconds": 60, "runtime": "/usr/local/bin/node" }`.

## Honest limits

- **Not a sandbox.** The child process is a lifecycle boundary (timeout, crash containment,
  kill-on-cancel), not a security one: model-written code runs with the user's privileges. The
  destructive annotation and the approval ask are the consent gate.
- **Scrubbed environment.** The child starts with an explicit allow-list (`PATH`, `HOME`,
  `TMPDIR`/`TEMP`/`TMP`, `SystemRoot`, `USERPROFILE`) and never inherits Lunate's environment, so
  provider keys cannot leak. The process itself is not isolated otherwise.
- **Canonical nested ids.** The structured `toolCalls` list carries the canonical `"<parent>/<n>"`
  ids the core assigned (`ToolResult.CallId`), so it agrees with the event stream and the session
  exactly — even when a bridged tool itself nests.
- **The runtime is external.** The extension ships no interpreter; `node` (or whatever `runtime`
  names) must be installed. A missing runtime is a teaching error with an install hint.
- **No in-process runtimes.** Roslyn scripting or Jint would be a separate opt-in and are not a
  sandbox either.

## Copy and extend

1. Copy this directory, rename the projects and namespaces, and update `extension.json`
   (`id`, `entryAssembly`, `apiVersion`, `tools`, `settingsSchema`).
2. Keep the handler shape: compose a script, run it through `core/process` with an environment
   allow-list, and bridge its calls through `ToolContext.ExecuteToolAsync` — approvals, hooks and
   cancellation stay in the core pipeline.
3. Keep the tests: they prove the bridge ids, approvals, the limits and the protocol.

See `docs/spec/extensibility.md` ("Code mode and hosted tools") and `docs/guide.md` for T-48.

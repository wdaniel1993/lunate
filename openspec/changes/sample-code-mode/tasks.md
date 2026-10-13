# Tasks — sample-code-mode

## 1. Core stubs (`Lunate.Agent`)

- [x] 1.1 `ToolStubParameter` (with `Note`), `ToolStub`, `ToolStubs.Describe(ITool)` per D1 (mapping table, degrade-to-any with a note, missing schema, `HasStructuredOutput`); XML docs; PublicAPI.Unshipped.
- [x] 1.2 `ToolContext.ToolStubs` init property; `AgentHarness.BuildToolContext` stamps the callable set (Direct/Programmatic/Deferred, registry order); null outside a run (D2).
- [x] 1.3 Units: describe mapping table + degrade note; stamped set composition, order, run/no-run, and a narrowed (subagent allow-list) registry.

## 2. Exposure spec closure

- [x] 2.1 Update `ToolExposure.Deferred` doc comment (D3) — no behaviour change.
- [x] 2.2 `agent-tools` delta: ADDED "Tool stubs"; MODIFIED "Tool exposure and registry declarations" (nested allows Direct/Programmatic/Deferred; Deferred never declared, stub-discoverable) with original scenarios preserved + one new scenario.

## 3. `core/process` service (Q1 outcome — sign-off)

- [x] 3.1 Contract in `Lunate.Extensibility.Abstractions` (`Services.cs`): `IProcessRunner`, `ProcessRunRequest`, `ProcessRunResult` per D10 (tree-kill/timeout/output-cap/cancellation semantics in XML docs; environment = exactly what the caller passes, `null` = inherit; Windows job path documented as the fallback); PublicAPI.Unshipped.
- [x] 3.2 Implementation `ProcessRunner` in `Lunate.Coding` (absorbs `PosixProcessTree` + the bash kill/cap/drain logic); register `core/process` in `ExtensionSession` beside `core/workspace`/`core/file-bus`.
- [x] 3.3 `BashTool` uses the runner internally (environment policy unchanged: inherit); remove the local `PosixProcessTree`; repoint `PosixProcessTreeTests` → runner tests (both runtime paths green).
- [x] 3.4 Service units: timeout kills the tree, output cap, cancellation, environment passthrough, exit codes.

## 4. Sample extension (`samples/extensions/code-mode`)

- [x] 4.1 `CodeModeExtension` (project references: **Abstractions only**): tool `run` → `codemode__run` (code: string required; timeoutSeconds?: integer), not read-only + destructive; settings `runtime`/`timeoutSeconds`/`maxOutputBytes`/`maxToolCalls` (D5); manifest declares the tool; README (what it proves, honest limits, try-it); captures `IProcessRunner` via `TryGetCoreService("core/process")` (D10).
- [x] 4.2 Runtime: embedded JS footer (protocol owner with the `hello` handshake; patches `console.*` + `process.stdout.write` into `log` frames; `__call`; final frame) + JS stub rendering from `ctx.ToolStubs` with **JSDoc doc comments** (tool/parameter descriptions, required/defaults/notes) (D4); temp script file cleaned in `finally`.
- [x] 4.3 Handler: run the child through the `core/process` runner with the **scrubbed environment allow-list** (D5), pump `call` frames ↔ `ctx.ExecuteToolAsync` responses on stdin, approvals flow through the pipeline, timeout → teaching error + `timedOut`, cancellation kills, output caps, **malformed protocol line → error result (never a crash)**, missing runtime → teaching error with install hint, `maxToolCalls` cap → teaching error frame, `StructuredContent` per D7.
- [x] 4.4 Sample tests (real `node`): bridge calls (`Programmatic` fixture + core tool; nested `<parent>/<n>` ids in events + session nested-call entries), one approval ask on a destructive nested call + decline → error result the script catches, **"always" on `codemode__run` does not suppress a later bridged ask**, structured content, timeout kill + no orphan pid, output cap, **protocol spoofing via `console.log` and `process.stdout.write` stays log frames**, malformed-line runtime → error result, bogus runtime path, scrubbed environment (planted `LUNATE_*` secret absent, PATH present), prelude JSDoc assertions.
- [x] 4.5 Solution entries (`lunate.sln`), AssemblyInfo parallelization attribute pattern per repo rule.

## 5. Docs

- [ ] 5.1 `docs/spec/extensibility.md`: code-mode section — stub primitive production path, the sample, honest limits; fitness-suite row updated; `core/process` named in the services section.
- [ ] 5.2 Sample README + settings documentation where the existing extension settings are documented.

## 6. Verify, review, archive

- [ ] 6.1 `scripts/verify.sh` green (both cultures); `openspec validate --all --strict` green.
- [ ] 6.2 Adversarial review; fix findings; focused re-review.
- [ ] 6.3 Archive; push; PR.

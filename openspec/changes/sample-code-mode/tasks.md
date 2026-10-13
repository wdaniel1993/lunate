# Tasks — sample-code-mode

## 1. Core stubs (`Lunate.Agent`)

- [ ] 1.1 `ToolStubParameter`, `ToolStub`, `ToolStubs.Describe(ITool)` per D1 (mapping table, degrade-to-any, missing schema, `HasStructuredOutput`); XML docs; PublicAPI.Unshipped.
- [ ] 1.2 `ToolContext.ToolStubs` init property; `AgentHarness.BuildToolContext` stamps the callable set (Direct/Programmatic/Deferred, registry order); null outside a run (D2).
- [ ] 1.3 Units: describe mapping table + degrade; stamped set composition, order and run/no-run.

## 2. Exposure spec closure

- [ ] 2.1 Update `ToolExposure.Deferred` doc comment (D3) — no behaviour change.
- [ ] 2.2 `agent-tools` delta: ADDED "Tool stubs"; MODIFIED "Tool exposure and registry declarations" (nested allows Direct/Programmatic/Deferred; Deferred never declared, stub-discoverable) with original scenarios preserved + one new scenario.

## 3. Shared kill helper (Q1 outcome)

- [ ] 3.1 Move `PosixProcessTree` → public `ProcessTree` in `Lunate.Agent` (POSIX ps-walk + Windows job path from `BashTool.KillTreeAsync`); XML docs carry the macOS-CI remark; PublicAPI.
- [ ] 3.2 `BashTool` uses it; remove the local type; move `PosixProcessTreeTests` → `Lunate.Agent.Tests/ProcessTreeTests` (both runtimes green).

## 4. Sample extension (`samples/extensions/code-mode`)

- [ ] 4.1 `CodeModeExtension`: tool `run` → `codemode__run` (code: string required; timeoutSeconds?: integer), not read-only + destructive; settings `runtime`/`timeoutSeconds`/`maxOutputBytes` (D5); manifest declares the tool; README (what it proves, honest limits, try-it).
- [ ] 4.2 Runtime: embedded JS footer (protocol owner, console capture, `__call`, final message) + JS stub rendering from `ctx.ToolStubs` (D4); temp script file cleaned in `finally`.
- [ ] 4.3 Handler: spawn child (`runtime` setting), pump stdout JSON lines ↔ `ctx.ExecuteToolAsync` responses on stdin, approve/prompt flows through the pipeline, timeout → `ProcessTree` kill → teaching error + `timedOut`, cancellation kills, output caps, node-missing teaching error, `StructuredContent` per D7.
- [ ] 4.4 Sample tests (real `node`): bridge calls (`Programmatic` fixture + core tool; nested `<parent>/<n>` ids in events), one approval ask on a destructive nested call + decline → error result the script catches, structured content, timeout kill + no orphan pid, output cap, bogus runtime path.
- [ ] 4.5 Solution entries (`lunate.sln`), AssemblyInfo parallelization attribute pattern per repo rule.

## 5. Docs

- [ ] 5.1 `docs/spec/extensibility.md`: code-mode section — stub primitive production path, the sample, honest limits; fitness-suite row updated.
- [ ] 5.2 Sample README + settings documentation where the existing extension settings are documented.

## 6. Verify, review, archive

- [ ] 6.1 `scripts/verify.sh` green (both cultures); `openspec validate --all --strict` green.
- [ ] 6.2 Adversarial review; fix findings; focused re-review.
- [ ] 6.3 Archive; push; PR.

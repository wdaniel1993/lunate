# Design — sample-code-mode

Sign-off items are listed as **Q1–Q5** where a call is genuinely open; the rest are recommendations to accept or amend.

## D1 — Typed stubs: the model, the mapping, the degrade rule

```csharp
public sealed record ToolStubParameter(
    string Name,
    string Type,          // "string" | "number" | "integer" | "boolean" | "array" | "object" | "enum" | "any"
    bool Required,
    string? Description,
    string? Default,      // raw JSON text of the schema default, null when absent
    IReadOnlyList<string>? Enum
);

public sealed record ToolStub(
    string Name,
    string Description,
    IReadOnlyList<ToolStubParameter> Parameters,
    bool HasStructuredOutput
);

public static class ToolStubs
{
    public static ToolStub Describe(ITool tool);
}
```

Mapping rules: object schemas only at the top (parameters); each property's `type` maps through the table above (`integer` folded into its own name — renderers decide); `required` from the schema's `required` array; `description` and `default` captured when present; `enum` captured as strings. **Deferred:** the runtime does not throw on unknown schema shapes — an unrecognised construct becomes `Type = "any"`, no `Type` becomes `"any"`, and a non-object or absent `ParametersSchema` yields zero parameters. `HasStructuredOutput` is true when the tool declares `OutputSchema`. The core gains no rendering: rendering stubs into a language is the consumer's job (D4).

## D2 — The stamped callable set (`ToolContext.ToolStubs`)

`AgentHarness` stamps `ToolContext.ToolStubs` (nullable list) with the stubs of every registered tool whose exposure is `Direct`, `Programmatic` or `Deferred`, in registration order. `ModelOnly` and `Hidden` never appear. Null outside a run (the same stamping model as `Subagents`/`FileChanges`). This is the list a code-mode extension describes to its runtime — and the reason `Deferred` exists at all.

## D3 — `Deferred`, closed (spec-only)

The implementation already lets `Deferred` pass the nested-execution gate (it refuses only `ModelOnly`/`Hidden`), but the spec sentence only names `Direct`/`Programmatic`, and the enum comment says *"nested execution refines this later"*. This change makes the contract explicit — nested execution allows **`Direct`, `Programmatic`, `Deferred`**; refuses `ModelOnly`, `Hidden` with an instructing error — and gives `Deferred` its meaning: **never declared to the model, discoverable through the run's stubs, callable from nested execution.** No behaviour change; spec and comment catch up.

## D4 — The sample's runtime: a `node` child process

The spec fixes the default (`separate process with timeout and resource limits`) but leaves the language to the extension. The sample picks **JavaScript via `node`** because `node` is already a hard CI/local dependency (the gate's markdownlint step), works identically on all three platforms, and needs no new package. The runtime contract:

- The model-written script runs from a temp file (OS temp directory, cleaned in a `finally`), `cwd` = the tool's working directory.
- **The script never talks to the core directly.** The extension generates a JS prelude from `ctx.ToolStubs` (`async function read({path}) { return await __call("read", {path}) }`-style, parameter names and required/optional taken from the stubs; JS is untyped, so `Type` informs docs/comments, not enforcement) and appends the model's code inside an `async` wrapper.
- A small runtime footer (shipped **inside the extension as an embedded resource**, not generated) owns the protocol: it replaces `console.*` with protocol log messages, exposes `__call`, runs the user script, and prints the final message.
- **The bridge**: the child's stdout carries **JSON lines only** (runtime-owned; the script's own output flows as protocol messages, so raw text can never corrupt the protocol). A script-side `__call({tool, args})` prints a `call` line; the extension handler reads it, runs `ctx.ExecuteToolAsync(tool, argsJson)` — the **full pipeline**: validation, hooks, approval prompt (a destructive nested tool asks the user, mid-script), cancellation, nested ids `"<parent>/<n>"` — and writes the `result` line back on the child's stdin.
- **Q4 covers the protocol details**; the invariants above are fixed.

## D5 — Timeout, limits, kill

Settings (per-extension settings JSON, pattern of the subagents sample): `runtime` (executable, default `"node"`), `timeoutSeconds` (default **30**, clamped 1–600), `maxOutputBytes` (default **65 536**, the script's captured output budget). The request may carry its own `timeoutSeconds`, clamped down only. On timeout the extension **kills the process tree** — via the shared `ProcessTree` (D10), children-first on POSIX, the framework job path on Windows — and returns a teaching error result with `timedOut: true`. A missing/broken runtime executable is a teaching error naming the `runtime` setting. `ct` cancellation kills the tree the same way.

## D6 — Approval, annotations, cancellation

`codemode__run` declares not-read-only + destructive (arbitrary code) → **asks** per call. Nested calls keep their own pipeline behaviour (a read-only nested call runs silently; a destructive nested call asks at that moment, labelled by the same prompt machinery as any nested call). Cancellation of the tool call kills the child and returns the cancelled result.

## D7 — Result shape

`Output` (model-facing): the script's `result` value when set, else the captured output, bounded by the normal tool-output budget. `StructuredContent`: `{ "exitCode": int?, "timedOut": bool, "result": JsonElement?, "output": string, "stderr": string, "toolCalls": [ { "callId": "<parent>/<n>", "tool": string, "isError": bool } ] }` — the tool-call list carries the canonical nested ids, so the structured result and the event stream agree. `Details` carries the full (capped) logs for the UI. Nonzero exit or timeout → error result, teaching message naming the cause.

## D8 — The sample's own shape

`samples/extensions/code-mode/`: `CodeModeExtension` + `CodeModeExtension.Tests`, manifest declares `run`, README (what it proves, the honest limits, how to try it). The extension registers exactly one tool; a fixture **`Programmatic` tool** (never declared to the model) lives in its test assembly, proving the exposure story (`codemode__run` is the model's only door; the fixture is callable only from scripts). A settings schema section documents the three keys.

## D9 — Tests

- **Core units**: `ToolStubs.Describe` across the mapping table (required/optional, enum, default, description, degrade-to-any, missing schema, `HasStructuredOutput`); the stamped set (exposures filtered, registry order, null outside a run).
- **Sample e2e (real `node` child)**: script calls the `Programmatic` fixture and a core tool through the bridge → nested events carry `"<parent>/<n>"` ids and the script sees the outputs; a destructive nested call raises **one approval ask** (recording approver) and a decline becomes an error result the script can catch; `StructuredContent` matches the script's `result` and the tool-call list; a `while(true)` script hits the timeout → teaching error, `timedOut`, and the process is gone (no orphan: asserted by pid probe); an output-flood script is capped; `runtime` pointed at a bogus executable → teaching error naming the setting; cancellation kills the tree.
- **Cross-platform**: the suite already requires `node` (the gate's docs-lint step); tests spawn it directly. Timeout test uses generous wall-clock margins so Windows load cannot flake it.

## D10 — Promote the process-tree kill (Q1)

`PosixProcessTree` (in `Lunate.Coding`, internal) is the culture-independent kill rule born from a real macOS-CI wedge. The sample needs the same behaviour, and extension authors writing process-spawning extensions will need it too — a copy in a sample is exactly the kind of duplication that rots. **Recommendation:** move it to `Lunate.Agent` as public `ProcessTree.Kill(pid)` (POSIX: the `ps`-walk; Windows: the framework job path, as today), switch `BashTool` to it, remove the local type, move its tests to `Lunate.Agent.Tests`. Alternative if you prefer the smallest diff: keep it sample-local as a documented copy. **Q1: promote (recommended) or copy?**

## D11 — Trust and honest limits (documented)

The child process is a **lifecycle boundary** (timeout, crash containment, kill-on-cancel), not a security sandbox: model-written code runs with the user's privileges and can do anything the user can. This is the in-process trust model the spec already states for extensions ("fully trusted", ALC isolates dependencies, not permissions). The README and the spec section say so plainly; the tool's destructive annotation + approval ask is the consent gate.

## D12 — Non-goals

A sandbox (D11), in-process runtimes (future opt-ins), provider-hosted tool passthrough, T-52, lazy/partial tool declaration beyond the stub list, language-specific stub renderers in core (core emits typed data; renderers live in consumers).

## Open sign-off questions

- **Q1** — ProcessTree promotion into `Lunate.Agent` (D10): promote (recommended) or sample-local copy?
- **Q2** — Stub model shape/name (`ToolStub`/`ToolStubParameter`/`ToolStubs` on `ToolContext`) — accept, or prefer different naming/placement?
- **Q3** — Defaults: `timeoutSeconds` 30 (1–600), `maxOutputBytes` 65 536, `runtime` `"node"`, `codemode__run` asking — accept or adjust?
- **Q4** — Protocol details are delegated to the apply (Q4 is informational): JSON-lines on stdout, results on stdin, runtime footer embedded in the extension, temp script cleanup in `finally`. Flag if you want any of this pinned differently.
- **Q5** — Scope check: stub generation covers parameters + structured-output flag only (no return-type modeling beyond that). Enough for v1?

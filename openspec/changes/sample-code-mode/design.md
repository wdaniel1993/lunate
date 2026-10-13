# Design — sample-code-mode

Sign-off: 2026-10-13, with adjustments. All items below are decided; the adjustment notes say **SIGN-OFF:** where the decision came from Daniel.

## D1 — Typed stubs: the model, the mapping, the degrade rule

```csharp
public sealed record ToolStubParameter(
    string Name,
    string Type,          // "string" | "number" | "integer" | "boolean" | "array" | "object" | "enum" | "any"
    bool Required,
    string? Description,
    string? Default,      // raw JSON text of the schema default, null when absent
    IReadOnlyList<string>? Enum,
    string? Note          // set when the mapping degraded (e.g. "unsupported schema construct: oneOf")
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

Mapping rules: object schemas only at the top (parameters); each property's `type` maps through the table above; `required` from the schema's `required` array; `description` and `default` captured when present; `enum` captured as strings. **SIGN-OFF Q5:** unknown constructs never throw — they degrade to `Type = "any"` **with a `Note` naming the construct** (e.g. `oneOf`, missing type); absent or non-object schema yields zero parameters. `HasStructuredOutput` is true when the tool declares `OutputSchema`. The core gains no rendering; rendering stubs into a language is the consumer's job (D4).

## D2 — The stamped callable set (`ToolContext.ToolStubs`)

`AgentHarness` stamps `ToolContext.ToolStubs` (nullable list) with the stubs of **exactly the tools callable via `ExecuteToolAsync` for that run** — the run's registry filtered by exposure (`Direct`, `Programmatic`, `Deferred`; never `ModelOnly`/`Hidden`), in registration order. Because the stamp is per run and from the run's own registry, **subagent allow-list narrowing is respected automatically** (a child run's stubs are its narrowed set). Null outside a run (the same stamping model as `Subagents`/`FileChanges`). This is the list a code-mode extension describes to its runtime — and the reason `Deferred` exists at all.

## D3 — `Deferred`, closed (spec-only)

The implementation already lets `Deferred` pass the nested-execution gate (it refuses only `ModelOnly`/`Hidden`), but the spec sentence only names `Direct`/`Programmatic`, and the enum comment says *"nested execution refines this later"*. This change makes the contract explicit — nested execution allows **`Direct`, `Programmatic`, `Deferred`**; refuses `ModelOnly`, `Hidden` with an instructing error — and gives `Deferred` its meaning: **never declared to the model, discoverable through the run's stubs, callable from nested execution.** No behaviour change; spec and comment catch up.

## D4 — The sample's runtime: a `node` child process

**SIGN-OFF Q1: the sample references only `Lunate.Extensibility.Abstractions` (ADR-0017) — it never project-references `Lunate.Agent`.** The runtime contract:

- The model-written script runs from a temp file (OS temp directory, cleaned in a `finally`), `cwd` = the tool's working directory.
- **The script never talks to the core directly.** The extension generates a JS prelude from `ctx.ToolStubs` and appends the model's code inside an `async` wrapper. **SIGN-OFF Q5:** the prelude carries the tool and parameter **descriptions as JSDoc doc comments** (plus required/optional, defaults and degrade `Note`s), so the script author — i.e. the model — sees the same documentation the model-facing declaration had.
- A runtime footer (shipped **inside the extension as an embedded resource**, not generated) owns the protocol: it patches `console.*` **and `process.stdout.write`** so script output becomes protocol frames, exposes `__call`, runs the user script, and prints the final message. Protocol frames are written through the saved original stdout handle.
- **Protocol (SIGN-OFF Q4, pinned):** every stdout line is one JSON frame; the first frame is the handshake `{ "type": "hello", "protocol": 1 }` (a mismatch is an error result, not a crash). Script output flows as `log` frames — **script text can never be mistaken for protocol** (a script printing protocol-shaped text still arrives as a `log` frame). A `call` frame (`{ "type": "call", "id": n, "tool": …, "args": … }`) is answered on the child's stdin with a `result` frame. A **malformed protocol line becomes an error result, never a crash**.
- **The bridge**: the extension handler reads `call` frames and runs `ctx.ExecuteToolAsync(tool, argsJson)` — the **full pipeline**: validation, hooks, approval prompt (a destructive nested tool asks the user, mid-script), cancellation, nested ids `"<parent>/<n>"`. Bridged calls are ordinary nested calls: their events and session entries roll up like any nested call. **SIGN-OFF Q3:** a `maxToolCalls` setting (default **100**) caps bridged calls per script; beyond it the bridge answers with a teaching error frame and the script continues.

## D5 — Timeout, limits, kill, environment

Settings (per-extension settings JSON, pattern of the subagents sample): `runtime` (executable, default `"node"`), `timeoutSeconds` (default **30**, clamped 1–600), `maxOutputBytes` (default **65 536**), `maxToolCalls` (default **100**). The request may carry its own `timeoutSeconds`, clamped down only. On timeout the extension **kills the process tree** through the shared core service (D10); the result is a teaching error with `timedOut: true`. **SIGN-OFF Q3:** the child starts with a **scrubbed environment** — an explicit allow-list (PATH, HOME, TMPDIR/TEMP/TMP, SystemRoot, …) — and **never inherits Lunate's environment**, so provider keys cannot leak into model-written code. A missing/broken runtime executable is a teaching error naming the `runtime` setting **with an install hint** (e.g. *install Node.js or set `runtime`*). `ct` cancellation kills the tree the same way.

## D6 — Approval, annotations, cancellation

`codemode__run` declares not-read-only + destructive (arbitrary code) → **asks** per call. **SIGN-OFF Q3:** an "always this session" grant given on `codemode__run` covers `codemode__run` only — **bridged tools keep their own approval decisions** (a later destructive bridged call still asks). Nested calls keep their own pipeline behaviour. Cancellation of the tool call kills the child and returns the cancelled result.

## D7 — Result shape

`Output` (model-facing): the script's `result` value when set, else the captured output, bounded by the normal tool-output budget. `StructuredContent`: `{ "exitCode": int?, "timedOut": bool, "result": JsonElement?, "output": string, "stderr": string, "toolCalls": [ { "callId": "<parent>/<n>", "tool": string, "isError": bool } ] }` — the tool-call list carries the canonical nested ids, so the structured result and the event stream agree. `Details` carries the full (capped) logs for the UI. Nonzero exit or timeout → error result, teaching message naming the cause.

## D8 — The sample's own shape

`samples/extensions/code-mode/`: `CodeModeExtension` + `CodeModeExtension.Tests` (project references: **Abstractions only**), manifest declares `run`, README (what it proves, the honest limits, how to try it). The extension registers exactly one tool; a fixture **`Programmatic` tool** (never declared to the model) lives in its test assembly, proving the exposure story. A settings schema section documents the four keys.

## D9 — Tests

- **Core units**: `ToolStubs.Describe` across the mapping table (required/optional, enum, default, description, degrade-to-any **with `Note`**, missing schema, `HasStructuredOutput`); the stamped set (exposures filtered, registry order, null outside a run, **narrowed child registry**).
- **Service units**: the `core/process` runner — timeout kills the tree, output cap, cancellation, environment passthrough, exit codes.
- **Sample e2e (real `node` child)**: script calls the `Programmatic` fixture and a core tool through the bridge → nested events carry `"<parent>/<n>"` ids, session nested-call entries exist, and the script sees the outputs; a destructive nested call raises **one approval ask** and a decline becomes an error result the script can catch; **"always" on the outer tool does not suppress a later bridged ask**; `StructuredContent` matches; a `while(true)` script hits the timeout → teaching error, `timedOut`, no orphan process (pid probe); output flood capped; **script output can't spoof protocol frames** (writes protocol-shaped text via `console.log` and `process.stdout.write`); a malformed-line runtime and a bogus `runtime` path → error results; **scrubbed environment** (a planted `LUNATE_*` secret is absent in the child, PATH present); `maxToolCalls` cap; the generated prelude contains the JSDoc doc comments.
- **Cross-platform**: the suite already requires `node`; timeout tests use generous margins.

## D10 — `core/process`: a core service (SIGN-OFF Q1)

Per ADR-0017, the process capability is a **core service**, not a public type in `Lunate.Agent`, and the sample never references `Lunate.Agent`:

- **Contract in `Lunate.Extensibility.Abstractions`** (`Services.cs`, next to `IFileChangeBus`): `IProcessRunner` — `RunAsync(ProcessRunRequest{ FileName, Arguments, WorkingDirectory, Environment?, Timeout?, OutputCapBytes? }, ct)` → `ProcessRunResult{ ExitCode, Stdout, Stderr, TimedOut, OutputCapped }`; semantics documented: **tree kill on timeout/cancel** (children-first POSIX `ps`-walk; Windows keeps the framework job path — documented as the fallback), output capped, environment = **exactly what the caller supplies** (`null` = inherit; extensions should pass an allow-list).
- **Implementation in `Lunate.Coding`**: `ProcessRunner` absorbs `PosixProcessTree` and the bash tool's kill/cap/drain logic; registered as **`core/process`** in `ExtensionSession` (next to `core/workspace`, `core/file-bus`); `BashTool` uses it internally, keeping its current environment policy (inherit) unchanged.
- **Consumption**: the sample captures `IProcessRunner` via `context.TryGetCoreService<IProcessRunner>("core/process")` at extension creation.
- PublicAPI: Abstractions gains the contract types; Coding internals stay internal.

## D11 — Trust and honest limits (documented)

The child process is a **lifecycle boundary** (timeout, crash containment, kill-on-cancel), not a security sandbox: model-written code runs with the user's privileges. This matches the in-process trust model the spec states for extensions; the tool's destructive annotation + approval ask is the consent gate. The README and the spec section say so plainly.

## D12 — Non-goals

A sandbox (D11), in-process runtimes (future opt-ins), provider-hosted tool passthrough, T-52, lazy/partial tool declaration beyond the stub list, language-specific stub renderers in core (core emits typed data; renderers live in consumers).

# Sample: code-mode (T-48)

## Why

T-48 (guide row 636): *"Programmatic tools via `ExecuteToolAsync`; child-process runtime"* — the fitness suite's code-mode proof. The extensibility spec (`docs/spec/extensibility.md`, "Code mode and hosted tools") already promises the core surface: *"The core provides only: `Programmatic`/`Deferred` exposure, namespaces, `OutputSchema` + `StructuredContent`, `ExecuteToolAsync`, and typed stub generation from JSON schemas."* Of that list, everything is built **except typed stub generation** — nothing in the product generates typed stubs from a tool's JSON schemas, and nothing hands a tool the list of programmatically callable tools it could describe. `ToolExposure.Deferred`'s doc comment also still says *"nested execution refines this later"* — an open T-38 leftover.

Per the fitness-suite rule, a sample that needs a workaround means the core is missing a primitive — and conversely a promised primitive without a reference consumer is unproven. This change adds the missing primitive (typed stubs + the stamped callable set) and proves the whole code-mode story with the reference extension: a tool that runs model-written code in a **child process**, where the code calls Lunate tools exclusively through `ExecuteToolAsync`.

Done-when: the sample works end to end — a script calls programmatic tools through the bridge (full pipeline: hooks, approval, nested ids), returns structured content, and dies cleanly at its timeout.

## What Changes

- **Core — typed stubs** (`Lunate.Agent`): `ToolStub` / `ToolStubParameter` models and generation from a tool's `ParametersSchema`/`OutputSchema` (string/number/integer/boolean/array/enum, required/optional, defaults and descriptions captured; unknown constructs degrade to an untyped parameter, never fail a run).
- **Core — the stamped callable set**: `ToolContext.ToolStubs` carries the run's programmatically callable tools (`Direct` + `Programmatic` + `Deferred`) in registry order; null outside a run. This is also the discovery surface `Deferred` was waiting for.
- **Spec closure — `Deferred`**: nested execution allows `Direct`/`Programmatic`/`Deferred` and refuses `ModelOnly`/`Hidden` (the implementation already behaves this way; the spec now says so, and `Deferred` is never declared but discoverable through the stubs).
- **Core — shared process-tree kill**: `PosixProcessTree` (a hard-won macOS-CI workaround from the bash tool) moves to `Lunate.Agent` as public `ProcessTree` so the bash tool and the new sample share one implementation instead of a copy.
- **Sample `code-mode`** (`samples/extensions/code-mode`): extension id `codemode`, one tool `run` → `codemode__run` (not read-only, destructive-capable → asks). It spawns a **child process** (`node` by default, configurable) that executes the model-written script; the extension **generates JavaScript stubs** for the stamped tools; every script-side tool call crosses a JSON-lines **bridge** back into `ExecuteToolAsync` — approvals, hooks, nested call ids (`<parent>/<n>`), cancellation and usage all stay in the core pipeline. Timeout, output caps and kill-on-timeout are the sample's resource limits; the result carries **`StructuredContent`** (`exitCode`, `timedOut`, `result`, logs, stderr, tool-call list).
- **Tests**: stub-generation units (schema mapping, degrade, stamped set composition); sample e2e through the real composition with a real `node` child: script calls a `Programmatic` fixture tool and a core tool through the bridge (nested ids + approval on a destructive nested call + decline becomes an error result), structured content, timeout kill (no orphan process), output cap, and a bogus `runtime` setting proving the teaching error.
- **Docs**: `docs/spec/extensibility.md` code-mode section (stub primitive + the sample's honest limits) and the fitness-suite row; sample README.

## Not in Scope

- **A security sandbox** — model-written code runs in a child process with the user's privileges; the process boundary is a lifecycle/timeout/crash boundary, not a security one. Honest limits are documented, matching the in-process trust model.
- **In-process runtimes** (Roslyn scripting, Jint) — remain future opt-ins documented as not-a-sandbox (spec).
- **Lazy declaration mechanics beyond discoverability** — `Deferred` gets its documented meaning (never declared, stub-listed, nested-callable); nothing more.
- **Provider-hosted tools** (MEAI hosted tool passthrough) and **T-52** (out-of-process extension host) — untouched.

## Impact

- `Lunate.Agent`: `ToolStub`/`ToolStubParameter`/`ToolStubs`, `ToolContext.ToolStubs`, `ProcessTree` (moved), PublicAPI updates.
- `Lunate.Coding`: `BashTool` switches to the shared `ProcessTree` (its local `PosixProcessTree` is removed); tests move.
- `samples/extensions/code-mode/**`: the sample extension + its tests; solution entries like the other samples.
- Specs: `agent-tools` (ADD "Tool stubs"; MODIFIED "Tool exposure and registry declarations"). No new packages; `Lunate.Extensibility.Abstractions` API unchanged (1.8).

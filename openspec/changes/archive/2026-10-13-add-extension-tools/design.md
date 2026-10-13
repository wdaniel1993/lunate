# Design: add-extension-tools

## Context

The core tool model (`Lunate.Agent.ITool`, agent-tools spec) is complete: name, description, hand-written schema, `ToolExposure`, `ToolNamespace`, `ToolAnnotations`, `OutputSchema`, concurrency, derived `Risk`, executability only through our own loop (ADR-0003). The registry rejects duplicates and keeps declaration order, but has **no removal**; the exposure gate covers nested/programmatic execution; `ToolContext` carries the working directory, run id, the file-change sink and nested `ExecuteToolAsync`. What is missing is the extension-facing registration surface, the adapter into that model, and the unload path. Samples compile against `Lunate.Extensibility.Abstractions`, which today is a leaf — the sign-off fixes the handler on the core `ToolContext`, so the contract assembly gains a `Lunate.Agent` reference (one way; no cycle: `Extensibility` already depends on both).

## Decisions

### D1 — Registration surface

`IExtensionContext.RegisterTool(ExtensionToolRegistration registration)` — registration only; must not start processes, sockets or timers. Called at extension creation; at most 32 registrations per extension. Returns void; refusals throw with an error naming the extension and the field/name.

### D2 — The registration record and the handler (contract)

```
ExtensionToolRegistration
  Name            required, the tool's local name; the model-facing name is <extension-id>__<name> (D7)
  Description     required, non-blank
  ParametersSchema required JsonElement, must parse as a JSON object
  OutputSchema    optional JsonElement
  Annotations     Lunate.Agent.ToolAnnotations? (null default — no hints)
  Exposure        Lunate.Agent.ToolExposure (Direct default)
  Risk            Lunate.Agent.ToolRisk? (explicit override for special needs)
  Handler         IExtensionToolHandler
```

`IExtensionToolHandler.InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken ct)` → `Task<ToolResult>` — **the same `ToolContext` and `ToolResult` as core tools**: one tool model, no mirrored context. The context's existing members cover everything an extension writer needs (`WorkingDirectory`, `RunId`, `FileChanges`, `ExecuteToolAsync`, `FileMutations`, `Progress`); zero new context surface is added. Because `ToolContext` is in `Lunate.Agent`, `Lunate.Extensibility.Abstractions` gains a `ProjectReference` to `Lunate.Agent` (one-way; `Extensibility` already references both; the layering checker gains the pair). Extension authors get `ToolContext` transitively through the contract assembly.

### D3 — Validation and duplicate rules

At load: blank/invalid names, non-object schemas, null handlers and unknown exposure values are refused with errors naming the extension and field. Duplicates (same name twice in one extension, across extensions, or colliding with a core tool name — after the D7 prefix, core collisions are impossible by construction; the check stays as defence) are refused naming both sources. Bounded at 32 per extension.

### D4 — Naming: literal prefix

The model-facing name is **`<extension-id>__<name>`** — the same literal `__` convention MCP tools use (`fake__echo`), sanitized to `[A-Za-z0-9_-]`, at most 64 characters (provider limits; no `:`). The namespace (grouping for discovery and rendering) is the extension id. The registration's local name is what the manifest declares.

### D5 — Approval defaults

Risk derives through the existing resolver: explicit `Risk` wins; otherwise annotations decide; **no annotations means not read-only and possibly destructive — the MCP default — so the call asks** (`ToolRiskResolver.FromAnnotations(null)` already yields `Write`; the requirement is spec'd and tested, not re-implemented).

### D6 — The manifest `tools` list, asymmetric

The manifest is what the user trusted before any code loaded, so:
- **Registered but not declared in the manifest → refused** at load (the manifest must cover everything the code contributes).
- **Declared but not registered → a warning, and the entry is dropped from the offered set** (an installed extension may predict tools from a newer API version; nothing insists).

### D7 — The adapter and composition

`ExtensionToolAdapter` maps a registration to `ITool` (all fields pass through; `Namespace` = extension id; `Name` = the prefixed name) and its `ExecuteAsync` invokes the handler with the **real** `ToolContext` and returns its `ToolResult`; output bounding, events, nested-call ids and the file-change stamping all happen in the existing pipeline. Composition: registrations enter the `ToolRegistry` after extension load and before the harness builds — the same window as MCP tools; extension tools first in registration order, then MCP tools. `ToolsChanged` fires for additions and removals.

### D8 — Unload, reload, and in-flight calls

`ToolRegistry` gains `Remove(name)` (declaration order preserved for the rest). The loader's existing `Unload(id)` path is wired at composition: unloading an extension **removes its tools from the registry**, fires `ToolsChanged`, and **cancels its in-flight calls** — the adapter tracks each extension's active calls under a per-extension lifetime source the unload path cancels; a cancelled call settles as a cancelled tool result, never a crash. Reload is unload+load with the same semantics. (Where the production unload entry point should be — `/extensions reload`-style command or trust-change path — is out of scope here if it doesn't already exist; the semantics are proven at the loader/composition seam plus a test through the extension session.)

### D9 — Resumed sessions render missing tools

A resumed session may contain calls to an extension tool that is no longer present. Rendering resolves by tool name with a default view when no renderer is registered — the rule the renderer spec already states for MCP tools — and this change extends the proof to extension tools: a session containing a recorded call to an absent extension tool renders without any renderer registered and without a crash.

### D10 — Handler failures

A throwing handler — or an in-flight call cancelled by extension unload — becomes an **error result with instructing text** (the model learns what happened and can adapt — ADR-0003's teaching-error spirit), logged; the run continues. Never a harness failure.

### D11 — API version and contracts

`ExtensionApi.Current` 1.6.0 → 1.7.0 (semver-minor). `PublicAPI.Unshipped.txt` updated in Abstractions, Agent (registry `Remove`) and Extensibility. No wire DTOs (registrations are runtime objects); the conformance suite is untouched.

### D12 — Proof

- Kit: a test extension registering tools; assertions: declared with the prefixed name and authored schema; invocation passes the real `ToolContext` (file-change publish lands on the bus with the full payload; nested `ExecuteToolAsync` uses `"<parent>/<n>"` ids); annotations reach `ToolCalling`; no-annotation tools ask (risk `Write`); `Hidden`/`Programmatic` are not declared; `OutputSchema`/`StructuredContent` pass through; handler throw → teaching error result; duplicates/malformed/undeclared-registration refusals name the field; unload removes the tools, fires `ToolsChanged`, cancels an in-flight call; a resumed session with a call to a missing tool renders.
- Coding e2e (print mode): the model calls an extension tool; result surfaces; worse-case paths (throw, unload mid-call) settle cleanly.
- Existing exposure-gate tests extend to extension-registered tools.

## Deviations

- Apply: `Lunate.Agent` joined `ExtensionLoadContext.SharedAssemblies` (without it, an `Agent.dll` beside the extension duplicates the type and handler identity breaks); the extensions delta gained a MODIFIED "Extension loading" requirement to match.
- Review fixes (this round): the collision guard (`ToolComposition.AddSkippingTaken`, all three modes, first-composition-wins diagnostic) + spec/delta updates (uniqueness scope, collision scenario, cooperative cancellation, "timed-out" removed, contract-assembly requirement synced); test additions (guard units, OutputSchema/StructuredContent by value, real interactive render path for an absent tool, unload-releases-the-assembly with a gcroot-verified drop discipline — the loader's `Load` result must not be awaited into a state machine that outlives the collection check; the collectible context object is unrooted but swept by a later GC pass, so the loop is deadline-based).

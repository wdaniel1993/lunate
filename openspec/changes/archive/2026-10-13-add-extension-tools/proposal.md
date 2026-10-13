# Proposal: add-extension-tools

## Why

T-47 (`sample-subagents`) needs the model to call a `spawn` tool the sample contributes — and **extensions cannot register tools today**. `IExtensionContext` offers hooks, background services, file-change subscriptions, MCP servers, renderers and status widgets; the manifest declares an optional `tools` list with no registration API behind it. The file-change-bus spec already presumes the missing surface ("extension and MCP tools that write files publish through their `ToolContext` file-change sink themselves"), and T-48 (code mode, nested `ExecuteToolAsync`), T-49 and T-51 need the same primitive. The fitness-suite rule applies: a sample that needs a workaround means the core is missing a primitive — this change adds it.

Split note: this is the first of two changes for card `t_eae1c3a5`. `sample-subagents` (the subagent host + the sample, with the four proven behaviors) follows once this surface is settled.

## What

- **`IExtensionContext.RegisterTool(ExtensionToolRegistration)`**: registration-time only (no processes, sockets, timers), ≤32 per extension, refused with errors naming the extension and field; duplicate names refused.
- **One tool model**: the handler is `IExtensionToolHandler.InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken ct) → Task<ToolResult>` — the **same `ToolContext` (incl. `ctx.FileChanges`) and `ToolResult` as core tools**, no separate context type. `Lunate.Extensibility.Abstractions` gains a one-way `Lunate.Agent` reference so extension authors see `ToolContext` transitively (layering checker updated).
- **Naming**: model-facing name `<extension-id>__<name>` (the literal `__` MCP uses), sanitized `[A-Za-z0-9_-]`, ≤64 chars, no `:`; core names reserved; the namespace (grouping) is the extension id.
- **Approval**: risk derives from annotations via the existing resolver; **no annotations = MCP defaults (not read-only, possibly destructive) → the call asks**.
- **Manifest rule (asymmetric)**: registered-but-not-declared → **refused** at load (the manifest is what the user trusted before code loaded); declared-but-not-registered → **warn and drop from the offered set**.
- **Adapters + composition**: registrations map to the core `ITool` model and enter the same `ToolRegistry` at composition, before the harness builds — exposure gates, ToolCalling annotations, approval, result bounding and tool events behave exactly as for core tools. Handler exceptions become **error results with teaching text** (ADR-0003); never a crash.
- **Unload/reload**: the registry gains `Remove`; unloading an extension removes its tools, fires `ToolsChanged`, and **cancels in-flight calls**; a resumed session with calls to a missing extension tool still renders (default renderer path).
- **API version**: `1.6.0` → `1.7.0`; PublicAPI updates; no new packages.
- **Proof**: a kit-driven test extension — declared/invoked, real ToolContext (file-change publish on the bus, nested calls, annotations → ToolCalling, no-annotation → asks), exposure gates, teaching errors, duplicate/malformed/undeclared refusals, unload + in-flight cancel, resumed-session rendering — plus a print-mode e2e.

## Impact

- `src/Lunate.Extensibility.Abstractions/`: `ExtensionToolRegistration`, `IExtensionToolHandler`, `IExtensionContext.RegisterTool`, API version, **Agent reference**.
- `src/Lunate.Agent/`: `ToolRegistry.Remove` (+ PublicAPI).
- `src/Lunate.Extensibility/`: `ExtensionContext` (registration + validation + manifest cross-check), `ExtensionToolAdapter` (incl. per-extension in-flight tracking), loader unload wiring.
- `src/Lunate.Coding/`: composition — `ExtensionSession` exposes the registered tools; the three modes add them (extension tools first, then MCP).
- `src/Lunate.Extensibility.Testing/`: kit loads/composes/unloads extension tools.
- `tests/Lunate.Coding.Tests/LayeringChecker.cs`: the new Abstractions → Agent pair.
- Specs: extensions (new "Extension tools" requirement), docs/spec/extensibility.md tool-model note, guide note.
- No ADR (ADR-0017 already fixes the architecture; this is a missing primitive of it).

## Out of scope

- The subagent host and the sample (`sample-subagents`, part 2 of the same card).
- A production `/extensions reload` command surface (the unload semantics are proven at the loader/composition seam).
- Out-of-process tool hosts (T-52).

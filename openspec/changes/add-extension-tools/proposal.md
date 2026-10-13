# Proposal: add-extension-tools

## Why

T-47 (`sample-subagents`) needs the model to call a `spawn` tool the sample contributes — and **extensions cannot register tools today**. `IExtensionContext` offers hooks, background services, file-change subscriptions, MCP servers, renderers and status widgets; the manifest declares an optional `tools` list with no registration API behind it. The file-change-bus spec already presumes the missing surface ("extension and MCP tools that write files publish through their `ToolContext` file-change sink themselves"), and T-48 (code mode, nested `ExecuteToolAsync`), T-49 and T-51 need the same primitive. The fitness-suite rule applies: a sample that needs a workaround means the core is missing a primitive — this change adds it.

Split note: this is the first of two changes for card `t_eae1c3a5`. `sample-subagents` (the subagent host + the sample, with the four proven behaviors) follows once this surface is settled.

## What

- **`IExtensionContext.RegisterTool(ExtensionToolRegistration)`**: registration-time only (no processes, sockets, timers), duplicates refused (within the extension, across extensions, and against core tool names), schema and names validated at load with errors naming the extension and field.
- **The contract mirrors the core tool model** in `Lunate.Extensibility.Abstractions`: name, description, hand-written `ParametersSchema` JSON, optional `OutputSchema`, annotations (`ReadOnly`/`Destructive`/`Idempotent`/`OpenWorld`), exposure (`Direct` default / `ModelOnly` / `Programmatic` / `Deferred` / `Hidden`), optional `Risk` override, namespace (defaults to the extension id).
- **Handler + context**: `IExtensionToolHandler.InvokeAsync(arguments, context, ct)`; the context exposes the working directory, the run id, the extension file-change publish sink (same stamped sink the core tools use) and nested `ExecuteToolAsync` (the same pipeline as core nested calls — validation, hooks, approval, cancellation, depth limits).
- **Adapters + composition**: registrations map to the core `ITool` model via adapters and enter the same `ToolRegistry` at composition, before the harness builds — exposure gates, ToolCalling annotations, approval, result bounding and the tool events all behave exactly as for core tools.
- **API version**: `1.6.0` → `1.7.0` (semver-minor); PublicAPI updates; no new packages.
- **Proof**: a kit-driven test extension registers tools — declared to the model, invoked, nested calls and file-change publishes work, duplicates and malformed declarations are refused, exposure gates hold — plus one print-mode e2e.

## Impact

- `src/Lunate.Extensibility.Abstractions/`: `ExtensionToolRegistration`, `IExtensionToolHandler`, `IExtensionToolContext`, `ExtensionToolResult`, mirrored enums, `IExtensionContext.RegisterTool`, API version.
- `src/Lunate.Extensibility/`: `ExtensionContext` (registration + validation), `ExtensionToolAdapter` (registration → `ITool`), loader validation, duplicate rules.
- `src/Lunate.Coding/`: composition — `ExtensionSession` exposes the registered tools; the three modes add them to the tool registry (mirroring the MCP-tool composition order).
- `src/Lunate.Extensibility.Testing/`: the kit loads and composes extension tools; test-run helpers.
- Specs: extensions (new "Extension tools" requirement), docs/spec/extensibility.md tool-model note, guide card table note.
- No ADR (ADR-0017 already fixes the architecture; this is a missing primitive of it).

## Out of scope

- The subagent host and the sample (`sample-subagents`, same card).
- Out-of-process tool hosts (T-52).
- Tool-registration by manifest only (registrations are code, the manifest `tools` list is validated against them — decision D7).

# Tasks: add-extension-tools

## 1. The contract (Abstractions + layer)

- [x] 1.1 `Lunate.Extensibility.Abstractions` gains a one-way reference to `Lunate.Agent`; `LayeringChecker` gains the pair.
- [x] 1.2 `ExtensionToolRegistration` (name, description, schema, optional output schema, `ToolAnnotations?`, `ToolExposure`, `ToolRisk?`, handler) + `IExtensionToolHandler.InvokeAsync(JsonElement, ToolContext, CancellationToken) → Task<ToolResult>` — the core types verbatim.
- [x] 1.3 `IExtensionContext.RegisterTool`; `ExtensionApi.Current` → `1.7.0`; PublicAPI.Unshipped updates (Abstractions, Agent, Extensibility).

## 2. Registry and unload

- [x] 2.1 `ToolRegistry.Remove(name)`: removes the tool, keeps declaration order for the rest, PublicAPI updated.
- [x] 2.2 `ExtensionToolAdapter` (registration → `ITool`: prefixed name, namespace = extension id, pass-through fields) with per-extension in-flight call tracking.
- [x] 2.3 Loader unload wiring: unload removes the extension's tools, fires `ToolsChanged`, cancels its in-flight calls (a cancelled call settles as a cancelled result, never a crash); reload = unload+load.
- [x] 2.4 Tests: removal keeps order; `ToolsChanged` on add and remove; an in-flight call is cancelled on unload.

## 3. Registration, validation, naming

- [x] 3.1 `ExtensionContext.RegisterTool`: registration-only, ≤32, refusals naming extension + field.
- [x] 3.2 Naming per D4: model-facing `<extension-id>__<name>`, sanitized `[A-Za-z0-9_-]`, ≤64, no `:`; core names reserved; namespace = extension id.
- [x] 3.3 Manifest cross-check (asymmetric, D6): registered-but-undeclared → load refused; declared-but-unregistered → warn + dropped from the offered set.
- [x] 3.4 Tests: malformed refusals; duplicate refusals; both manifest mismatches.

## 4. Composition and pipeline behavior

- [x] 4.1 `ExtensionSession` exposes composed extension tools; the three modes add them (extension tools first, then MCP) per D7.
- [x] 4.2 Same pipeline: annotations → `ToolCalling` payload; no-annotation tools ask (risk `Write`, MCP defaults); approval; result bounding; `OutputSchema`/`StructuredContent`; handler throw → teaching error result, never a crash (D5, D10).
- [x] 4.3 Resumed-session rendering: a recorded call to an absent extension tool renders via the default view (D9).
- [x] 4.4 Kit: loads/composes/unloads extension tools.

## 5. Proof

- [x] 5.1 Kit test extension: declared with prefixed name + authored schema; invocation receives the real `ToolContext`; file-change publish lands on the bus (one event, full payload); nested `ExecuteToolAsync` with `"<parent>/<n>"` ids; exposure gates (`Hidden`/`Programmatic` not declared).
- [x] 5.2 Print-mode e2e: the model calls an extension tool; result surfaces; a throwing tool yields the teaching error; unload mid-call settles cleanly.
- [x] 5.3 Process-spawning tests join the `mcp-processes` collection.

## 6. Specs, docs, gate

- [x] 6.1 Extensions delta: the "Extension tools" requirement updated to the sign-off text (ToolContext/ToolResult, naming grammar, approval defaults, asymmetric manifest rule, teaching errors, unload + in-flight cancel, resumed rendering).
- [x] 6.2 docs/spec/extensibility.md tool-model section; guide note.
- [x] 6.3 `scripts/verify.sh` (both cultures), `openspec validate --all --strict` clean, csharpier + markdownlint clean.

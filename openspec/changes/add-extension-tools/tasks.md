# Tasks: add-extension-tools

## 1. The contract (Abstractions)

- [ ] 1.1 `ExtensionToolRegistration` + `IExtensionToolHandler` + `ExtensionToolResult` + mirrored enums (annotations, exposure, risk, kind) per design D2.
- [ ] 1.2 `IExtensionToolContext` (working directory, run id, file-change publish, nested `ExecuteToolAsync`) per D3.
- [ ] 1.3 `IExtensionContext.RegisterTool`; `ExtensionApi.Current` → `1.7.0`; PublicAPI.Unshipped updates.

## 2. Registration and validation (Extensibility)

- [ ] 2.1 `ExtensionContext.RegisterTool`: registration-only, bounded (32), refusals naming the extension and field/name.
- [ ] 2.2 Duplicate rules: within the extension, across extensions, against core names — refused naming both sources.
- [ ] 2.3 Manifest `tools` mismatch logged as a warning (D7).
- [ ] 2.4 Tests: malformed name/schema/handler refused; duplicates refused; the bound holds; mismatch warns.

## 3. The adapter and composition

- [ ] 3.1 `ExtensionToolAdapter` mapping a registration to `ITool` (enums, namespace default = extension id, risk override, result mapping) per D4.
- [ ] 3.2 `ExtensionSession` exposes composed extension tools; the three modes add them (extension tools first, then MCP) per D5.
- [ ] 3.3 Execution through the same pipeline: annotations in the `ToolCalling` payload, approval, result bounding, error results on handler throw, `ToolsChanged` fires.
- [ ] 3.4 Kit: loads and composes extension tools.

## 4. Proof

- [ ] 4.1 Kit test extension: declaration with authored schema, invocation, exposure gates (`Hidden`/`Programmatic` not declared), `OutputSchema`/`StructuredContent`, nested `ExecuteToolAsync` with `"<parent>/<n>"` ids.
- [ ] 4.2 The context's file-change publish lands on the bus: one event, full payload (absolute + relative path, workspace id, run id, kind).
- [ ] 4.3 Print-mode e2e: the model calls an extension tool; result surfaces; no extension → unchanged.
- [ ] 4.4 Process-spawning tests join the `mcp-processes` collection.

## 5. Specs and docs

- [ ] 5.1 Extensions delta: new "Extension tools" requirement with scenarios (registered tool declared and runs; annotations reach ToolCalling; exposure gates; duplicates refused; the context publishes file changes and runs nested calls).
- [ ] 5.2 docs/spec/extensibility.md: tool-model section gains the registration surface; guide note.
- [ ] 5.3 Gate: `scripts/verify.sh` (both cultures), `openspec validate --all --strict` clean, csharpier + markdownlint clean.

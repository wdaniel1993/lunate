# Design: add-extension-tools

## Context

The core tool model (`Lunate.Agent.ITool`, agent-tools spec) is complete: name, description, hand-written schema, `ToolExposure`, `ToolNamespace`, `ToolAnnotations`, `OutputSchema`, concurrency, derived `Risk`, executability only through our own loop (ADR-0003). The registry rejects duplicates, keeps declaration order, and the exposure gate covers nested/programmatic execution. `ToolContext` carries the working directory, run id, the file-change sink and nested `ExecuteToolAsync`. What is missing is the extension-facing registration surface and the adapter into that model. Samples compile against `Lunate.Extensibility.Abstractions` only, so the surface must live there (with mirrored enums — the assembly is a leaf).

## Decisions

### D1 — Registration surface

`IExtensionContext.RegisterTool(ExtensionToolRegistration registration)` — registration only; must not start processes, sockets or timers (the same rule every other registration carries). Called at extension creation; a max of 32 registrations per extension (mirrors the bounded-records idiom). Returns void; refusals throw with an error naming the extension and the field/name.

### D2 — The registration record (contract)

```
ExtensionToolRegistration
  Name            required, model-facing, unique in the registry, validated pattern
  Description     required, non-blank
  ParametersSchema required JsonElement, must parse as a JSON object
  OutputSchema    optional JsonElement
  Annotations     flags enum (None default): ReadOnly, Destructive, Idempotent, OpenWorld
  Exposure        mirror of ToolExposure, Direct default
  Risk            optional explicit override (enum mirror); default derives from annotations (same resolver rule as core)
  Namespace       optional; defaults to the extension id (discovery grouping, mirroring how MCP servers namespace)
  Handler         IExtensionToolHandler
```

`IExtensionToolHandler.InvokeAsync(JsonElement arguments, IExtensionToolContext context, CancellationToken ct)` returns `ExtensionToolResult(Output, IsError = false)` with optional `StructuredContent`.

### D3 — The tool context

```
IExtensionToolContext
  WorkingDirectory  string      — the run's workspace root
  RunId             string?     — the calling run (null outside a run)
  FileChanges       sink        — publishes through the SAME stamped sink the core tools use
                                  (the file-change-bus rule: writer tools publish through their own context)
  ExecuteToolAsync(name, args, ct) — nested call through the same pipeline: validation, hooks,
                                  approval, cancellation, depth limits, "<parent>/<n>" ids
```

The file-change mirror is minimal: `PublishFileChanged(absolutePath, kind)` with a mirrored kind enum; the adapter forwards to the run's stamped `ToolContext.FileChanges` (workspace id, relative path and run id are stamped by the harness as for core tools — no re-implementation).

### D4 — The adapter

`ExtensionToolAdapter` (Lunate.Extensibility) maps one registration to `ITool`: name/description passed through; `ParametersSchema`/`OutputSchema` as authored; annotations and risk mapped to the core enums; namespace mapped to `ToolNamespace`; `ExecuteAsync` builds the `IExtensionToolContext` around the real `ToolContext` (working dir, run id, sink, nested delegate) and invokes the handler. Result mapping: `Output` → the model-facing text, `IsError` → the error flag, `StructuredContent` → `ToolResult.StructuredContent`; output bounding happens in the core pipeline as for every tool.

### D5 — Composition

Registrations are composed into the `ToolRegistry` **after extension load and before the harness builds** — the same window in which MCP tools are composed; extension tools first, in registration order, then MCP tools, keeping the existing "extension tools first" ordering rule. `ExtensionSession` exposes the composed tools; the three modes add them where they add MCP tools today. The `ToolsChanged` hook fires as it does for any composition change. An extension tool whose handler throws produces an error result with instructing text and a report — never a harness failure (matches core tool semantics).

### D6 — Validation and duplicate rules

At load: blank/invalid names, non-object schemas, unknown exposure values and null handlers are refused with errors naming the extension and field. Duplicates: the same name twice within one extension, across extensions, or against a core tool name → refused, naming both sources; the core registry's own duplicate rule is the second line of defence. The tool list is bounded (32) to keep startup bounded with 10 installed extensions.

### D7 — The manifest `tools` list

The manifest keeps declaring contributed tool names (discovery); a mismatch (declared but not registered, or registered but not declared) is **logged as a warning**, not refused — refused would break installed extensions updating across API versions, and the registry itself is already authoritative. (Decision to confirm at sign-off.)

### D8 — API version and contracts

`ExtensionApi.Current` 1.6.0 → 1.7.0 (semver-minor: new members on `IExtensionContext`, new types). `PublicAPI.Unshipped.txt` updated in Abstractions and Extensibility. No wire DTOs are added (registrations are runtime objects with delegates; nothing round-trips through JSON), so the conformance suite is untouched.

### D9 — Proof

- Kit: a test extension registering 2–3 tools; assertions: declared to the model with the authored schema, invocation works, annotations reach the `ToolCalling` payload, `Hidden`/`Programmatic` are not declared, `OutputSchema` + `StructuredContent` pass through, the context's `FileChanges` publish lands on the bus (one event, full payload), nested `ExecuteToolAsync` runs with `"<parent>/<n>"` ids, duplicates and malformed registrations are refused naming the field.
- Coding e2e (print mode): the model calls an extension tool and the result surfaces; with the feature off (no extension) nothing changes.
- The existing exposure-gate tests extend to extension-registered tools.

## Deviations

None yet.

## Open items for sign-off

- D7 (manifest mismatch = warn vs refused).
- Tool naming: plain declared name (no `extension-id` prefix) with the namespace carrying the grouping — recommended, mirroring MCP where the server IS the namespace. An id prefix would make every prompt mention unwieldy longer.

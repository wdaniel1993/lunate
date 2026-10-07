# Design — add-extensibility-architecture

## Why docs first, formats second

The format migrations (Part A of the request) change events, tool contracts and the session schema. Those changes need a stable reference to point at: the ADR fixes the decisions, the spec fixes the hook/tool/service model, and the formats then cite both. Splitting keeps each change reviewable and lets the migrations reference "ADR-0017" and the hook catalogue instead of restating them.

## Where each fact lives (one home per fact)

- **ADR-0017**: durable decisions — contract assembly, ALC sharing list, trust model, hook semantics classes, JSON boundary, out-of-process constraint. At the repo root like every ADR.
- **`docs/spec/extensibility.md`**: the working architecture spec — the hook catalogue (table), tool model summary (the byte-level formats stay in the OpenSpec specs), services, UI by mode, code mode, fitness suite. Behavioural requirements that are already testable live in OpenSpec specs (`agent-events`, `agent-tools`, `agent-sessions`); the spec links there instead of duplicating.
- **Guide**: positioning and roadmap only — the extensions section gains a pointer to the spec, the cards table gains the series, non-goals get the subagent line.
- **Cards**: T-24 (extension loader) and T-34 (extension authoring) are replaced by T-36…T-51:

| Card | Scope | Depends on |
| --- | --- | --- |
| T-36 | Contract assembly `Lunate.Extensibility.Abstractions` (semver, PublicAPI) + ALC sharing + manifest + lifecycle + settings/secrets | T-09…T-11 |
| T-37 | Hook runner: catalogue semantics, order, failure policy, timeouts | T-36 |
| T-38 | Tool model extensions: exposure, namespaces, annotations, OutputSchema/StructuredContent, ExecuteToolAsync wiring, per-tool concurrency | T-37 |
| T-39 | Services: background services, file change bus, mutation queue, model/service registry | T-37 |
| T-40 | UI abstraction across modes (`IUserInteraction`, renderers, degradation) | T-18, T-19, T-39 |
| T-41 | MCP from extensions (RegisterMcpServer, namespace, exposure) | T-39 |
| T-42 | `Lunate.Extensibility.Testing` + template project | T-37 |
| T-43…T-50 | One card per reference extension: permission-gate, memory-provider, lsp-diagnostics, mcp-from-extension, subagents, code-mode, model-router, ui-showcase | their feature cards |
| T-51 | Out-of-process host (`Lunate.Extensibility.Remote`, JSON-RPC over stdio) — design only until scheduled | T-37…T-42 |

## What the migrations (next change) will pin

Listed here so the split is honest; all implementation detail lands in `add-extension-formats`:

- Events: `SessionId`, `ParentRunId`, `Source` (init properties on the base), `ParentToolCallId` on tool events, nested call ids `"<parent>/<n>"`, usage roll-up to the owning run, consumers ignore unknown kinds.
- Tools: `Exposure`, `Namespace`, `Annotations`, `OutputSchema`, `ToolResult.StructuredContent`/`Usage`, `ToolContext.ExecuteToolAsync`/`RunId`/progress/`IFileMutationQueue`, per-tool concurrency.
- Sessions: schema 2 — namespaced `ext/` entries with opaque payloads preserved byte-for-byte, new core entries (active tools, prompt section, child session), bounded nested-call records, hosted-content preservation, migration policy under ADR-0018.

## Deliberate non-goals for this change

- No `src/` changes; no OpenSpec spec deltas.
- Hook DTO byte shapes, UI widget protocol details and code-mode runtime choices are pinned by their cards, not here.

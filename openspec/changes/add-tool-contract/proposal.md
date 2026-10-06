## Why

Layer 2 opens with the tool surface. The loop (T-09) runs tools itself (ADR-0003): it needs a fixed contract for what a tool is, how it is declared to the model, how it is found by name, and how its output is bounded before it enters the history. Building this contract first (card T-08) lets the loop, the four core tools (T-12 to T-15) and the MCP wrapper (T-29) be written against tested shapes — and keeps Microsoft.Extensions.AI out of invocation entirely: the model sees declaration-only adapters, and nothing reflective ever builds a tool.

## What Changes

- **Tool contract** in `Lunate.Agent`: `ToolRisk` (`ReadOnly`/`Write`/`Execute`), `ITool` (name, description, hand-written `ParametersSchema` JSON, risk, `ExecuteAsync`), `ToolResult` (output, error flag, UI-only `Details`), `ToolContext` (working directory, `IAgentEvents`). No reflection: schemas are written by hand.
- **`ToolDeclaration`** — an internal `AIFunction` adapter that presents name, description and schema to the model **unchanged**; every invocation path through Microsoft.Extensions.AI throws with guidance (the loop invokes tools, never MEAI).
- **`ToolRegistry`** — tools by name: registration rejects duplicates, lookup by name, and the declaration list (registration order) that T-09 feeds into `ChatOptions`.
- **Tool output truncation** — `ToolOutput.Truncate` (default 30,000 characters): short output passes through untouched; long output is cut in the middle with a marker and never splits a UTF-16 surrogate pair. The loop applies it before appending tool results (T-09).
- Out of scope: the loop (T-09), the four core tools (T-12 to T-15), MCP wrapping (T-29), approvals (T-21).

## Capabilities

### New Capabilities
- `agent-tools`: the tool contract, the declaration-only model adapter, the registry and output truncation.

### Modified Capabilities
- None.

## Impact

- `src/Lunate.Agent` (new public types + `PublicAPI.Unshipped.txt`), `tests/Lunate.Agent.Tests`. No new packages; `System.Text.Json` is in-box.

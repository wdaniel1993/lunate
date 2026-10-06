# ADR-0012: Tool contract and declaration adapter

- Status: accepted — 2026-10-06 (maintainer sign-off; change merged)
- Date: 2026-10-06
- Relates to: ADR-0003 (own loop), ADR-0013 (agent loop)

## Context

The loop runs tools itself (ADR-0003): approvals, events, cancellation and error handling all happen between tool calls, so `FunctionInvokingChatClient` and `AIFunctionFactory` are banned. That leaves three questions the codebase must answer once, before the loop and the tools are written: what a tool is (our type vs Microsoft.Extensions.AI's), how the model learns about it, and how much of a tool's output may enter the history.

## Decision

1. **Tools are our type.** `ITool` carries what Microsoft.Extensions.AI does not model: a `ToolRisk` for the approval policy, a `ToolContext` with the working directory and the event sink, and a `ToolResult` whose `Details` (diffs and similar) are UI-only. Schemas are hand-written JSON — no reflection.
2. **The model sees declaration-only adapters.** `ToolDeclaration : AIFunction` presents name, description and the schema exactly as authored (the schema is a raw-text clone) and throws on every invocation path through Microsoft.Extensions.AI; the loop invokes `ITool.ExecuteAsync` directly.
3. **Output is bounded at the loop boundary.** `ToolOutput.Truncate` (default 30,000 characters) keeps head and tail with an explicit omitted-count marker, cut surrogate-safely; the model never receives the full unbounded output.

## Consequences

- The loop (T-09), the four core tools (T-12 to T-15) and MCP wrapping (T-29) compile against one tested contract.
- Schema fidelity is locked by tests: what the author writes is what the model sees.
- Middle truncation keeps the informative ends of long outputs; the omitted count is stated, and the UI can still surface full details through `Details`.

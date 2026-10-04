# 0002 — Three-layer project graph and Microsoft.Extensions.AI model types

- Status: accepted
- Date: 2026-10-04

## Context

Sigma needs a dependency structure that keeps the agent core reusable and testable, and a single message model that fits the ecosystem (MCP SDK, telemetry middleware) without a mapping layer of our own.

## Decision

- The project graph is downward-only: `Sigma.Ai` ← `Sigma.Agent` ← `Sigma.Protocols` / `Sigma.Coding`; `Sigma.Tui` references no other Sigma project; `Sigma.Agent` references only `Microsoft.Extensions.AI.Abstractions`, `Sigma.Ai` and the BCL.
- All message and model types are Microsoft.Extensions.AI types (`IChatClient`, `ChatMessage`, `AIContent`, `ChatOptions`, `ChatResponseUpdate`). Own types exist only for tools (`ITool`) and events (`AgentEvent`).
- The loop runs tools itself: never `FunctionInvokingChatClient`, never `AIFunctionFactory`.

## Consequences

- The layering is enforced by an architecture test; violations fail the build.
- Public API changes surface as `PublicAPI.Unshipped.txt` diffs.
- MCP tools and telemetry integrate without adapters; the TUI, print mode, ACP and any future frontend consume `AgentEvent`s only.

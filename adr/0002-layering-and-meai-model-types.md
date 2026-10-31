# 0002 — Three-layer project graph and Microsoft.Extensions.AI model types

- Status: accepted
- Date: 2026-10-04

## Context

Lunate needs a dependency structure that keeps the agent core reusable and testable, and a single message model that fits the ecosystem (MCP SDK, telemetry middleware) without a mapping layer of our own.

## Decision

- The project graph is downward-only: `Lunate.Ai` ← `Lunate.Agent` ← `Lunate.Protocols` / `Lunate.Coding`; `Lunate.Tui` references no other Lunate project; `Lunate.Agent` references only `Microsoft.Extensions.AI.Abstractions`, `Lunate.Ai` and the BCL.
- All message and model types are Microsoft.Extensions.AI types (`IChatClient`, `ChatMessage`, `AIContent`, `ChatOptions`, `ChatResponseUpdate`). Own types exist only for tools (`ITool`) and events (`AgentEvent`).
- The loop runs tools itself: never `FunctionInvokingChatClient`, never `AIFunctionFactory`.

## Consequences

- The layering is enforced by an architecture test; violations fail the build.
- Public API changes surface as `PublicAPI.Unshipped.txt` diffs.
- MCP tools and telemetry integrate without adapters; the TUI, print mode, ACP and any future frontend consume `AgentEvent`s only.

## Addendum (2026-10-31) — a shared bottom assembly: `Lunate.Contracts`

### Context

`Lunate.Extensibility.Abstractions` references `Lunate.Agent` today, and `LayeringChecker` permits the edge; `ExtensionToolRegistration` and `ToolActivationEvaluator` are its two consumers. The decision layer's sign-off puts `IDecisionService` in Abstractions and its implementation in Agent, which that edge makes impossible: a contract assembly that depends on the implementation cannot be the published extension boundary, and #237 is about to publish it as exactly that. The obvious repair — Agent → Abstractions — is forbidden: the core would depend on the extension concept, the inversion the T-49 layering rule was written to prevent. A contract both sides need belongs *below* both.

### Decision

- New bottom assembly `Lunate.Contracts`, referencing only the BCL and `Microsoft.Extensions.AI.Abstractions`. It owns the types both sides need: `ITool`, `ToolContext`, `ToolResult`/`ToolResultView` including `StructuredContent`, tool declarations and annotations, the **complete `AgentEvent` hierarchy** — the closed set of sealed records declared together and pattern-matched as a union, so it moves whole; `AgentEventChannel`, `EventSequenceValidator` and the event sinks stay in `Lunate.Agent` — and the decision request/result contract.
- `Lunate.Agent` → `Lunate.Contracts`. `Lunate.Extensibility.Abstractions` → `Lunate.Contracts` and **not** `Lunate.Agent`. Agent and Extensibility.Abstractions never reference each other in either direction.
- `LayeringChecker` enforces all three edges: both downward edges allowed, the Agent ↔ Abstractions pair forbidden both ways; extension projects that referenced Agent for one of the moved types move to Contracts.
- `IDecisionService` stays in `Lunate.Extensibility.Abstractions`, typed with the contract's request/result types. The host bridges it to Agent's implementation inside `Lunate.Extensibility`, the only project that references both.
- **One identity across the load boundary.** `Lunate.Contracts` joins `HostOwnedAssemblies.Names` beside `Lunate.Extensibility.Abstractions`, `Lunate.Agent`, `Lunate.Ai` where a signature reaches it, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json`: a copy an extension's directory or a locked package carries is never loaded, so `ITool` and `AgentEvent` keep one identity. A single-file extension compiles against Contracts and Abstractions. Test: an extension compiled against the old location (its `ITool` in `Lunate.Agent`) loads through the type forward and its tool registers.
- Compatibility: every moved type keeps `[TypeForwardedTo]` in its old assembly **until `ExtensionApi`'s next major**, and the forwards are removed together with that bump — they cost nothing until then. `ExtensionApi.Current` moves 1.13.0 → 1.14.0 (minor): new surface, no removal. `Lunate.Contracts` and `Lunate.Extensibility.Abstractions` are versioned in lockstep under `ExtensionApi`.
- #237 publishes `Lunate.Contracts` and `Lunate.Extensibility.Abstractions` together as the stable contract; `Lunate.Agent` and `Lunate.Extensibility.Testing` stay preview.
- After the move the ADR-0026 condition is re-checked: "Roslyn and BuiltIn reference only the Abstractions" becomes true in substance, because the Abstractions no longer drag the agent in behind them.

### Consequences

- The layering test gains a forbidden edge and loses an allowed one; a new project joins build, package, API-shipping and packaging lists, and the host-owned list.
- `Lunate.Contracts` is the only assembly a third party needs for the types an extension exchanges with the host; `Lunate.Extensibility.Abstractions` adds the services on top, and `Lunate.Extensibility` is the single bridge between contract and agent. Any future host-side service follows the same shape.
- Moving `AgentEvent` changes its assembly identity for already-compiled code — the forward covers loading; reflection that compares assembly names sees the old name until the forwards are removed.
- The forwards outlive a minor release by design. The bump that removes them is `ExtensionApi`'s next major, so the removal is announced by the version an extension already declares.

### Alternatives rejected

- **Agent → Extensibility.Abstractions** — the core would depend on the extension concept (T-49).
- **`IDecisionService` in Agent** — #237 would publish a contract extensions cannot use without referencing the agent, against the sign-off.
- **Duplicated types in both assemblies** — two homes, two copies.
- **Splitting `AgentEvent`** — the hierarchy is a closed union that `EventSequenceValidator` and the front ends pattern-match; a split turns one exhaustive match into two partial ones.

## Context

Guide Layer 2, "Events (the only output of the core)": the event table fixes the set and its AG-UI/ACP/TUI mapping; `ToolContext` already takes `IAgentEvents`; `AgentHarness.RunAsync` will expose `IAsyncEnumerable<AgentEvent>` (T-09). This change builds the types, the emitter and the sequence rules — nothing that emits them yet.

## Goals / Non-Goals

**Goals:** the exact event contract (names, fields, order rules) with a testable emission path; the closed set stays union-ready.

**Non-Goals:** the loop (T-09), tools (T-08), session persistence and spans (T-09), TUI rendering (Phase 4).

## Decisions

- **Shape**: abstract `AgentEvent` record; one sealed derived record per row of the guide's table; extension events derive from `ExtensionEvent : AgentEvent` so mappers can forward or filter extensions generically. Every event carries `RunId`; fields are event-specific (`StopReason`, `MessageId`, `CallId`, `ToolName`, `Text`, `Details`, `Usage`, …). No interfaces for events; the set changes only through the spec.
- **Emitter**: `IAgentEvents` has a single `Emit(AgentEvent)` method — tiny surface, union-ready; typed construction happens at call sites. Channel implementation (`internal`) for the harness boundary: ordered, bounded, completes on `Complete()`; tests use a recording implementation that exposes `IReadOnlyList<AgentEvent>`.
- **Sequence rules**: an `internal` `EventSequenceValidator` checks a sequence against the rules (start/terminal uniqueness, text bracketing per message, tool call ordering per call id, no events after terminal). It is reused by T-09's loop tests on real runs.
- **Naming**: names follow the guide's table (AG-UI alignment checked against the pinned AG-UI spec when the mappers are written — T-22/T-27); this change pins the Lunate-side names only.

## Risks / Trade-offs

- [Field set too thin] → fields cover the table's needs (TUI footer, diff details, stop reasons); adding fields is additive and tracked by PublicAPI.
- [Validator over-constrains T-09] → rules are exactly the spec's; the validator reports violations without throwing, so the loop tests decide severity.

## Migration Plan

Not applicable — additive.

## Open Questions

- None blocking.

## Why

Layer 2 opens with its only output: events. `Lunate.Agent` emits a closed set of events that the TUI, print mode and ACP consume, and tools and the loop emit through them. Building the event types first (card T-07) lets the loop (T-09), the tools (T-08) and every frontend be written against a fixed, tested contract — and the AG-UI-aligned names keep the future mappers thin.

## What Changes

- **`AgentEvent`** — a closed, sealed set of event records in `Lunate.Agent`, named and sequenced like AG-UI's: `RunStarted`, `RunFinished(stopReason)`, `RunError`, `TextMessageStart`/`Content`/`End`, `ToolCallStart`/`Args`/`End`, `ToolCallResult` (with UI-only `Details`), plus Lunate extension events (`ApprovalRequested`, `UsageUpdated`, `Retrying`, `CompactionApplied`, `StepLimitReached`) under a common extension base. Kept closed so it can become a C# 15 union at .NET 11 GA (guide note).
- **`IAgentEvents`** — the emitter interface (`Emit(AgentEvent)`), used by the loop and handed to tools via `ToolContext`; a channel-backed implementation exposes events as `IAsyncEnumerable` at the harness boundary; tests use a recording implementation.
- **Sequence rules** — encoded in a validator and covered by tests: a run begins with `RunStarted`; exactly one terminal event ends it; text content is bracketed per message; tool events follow start → args → end → result per call id; nothing follows the terminal event.
- Out of scope: the loop that emits them (T-09), tool implementations (T-08), session persistence of events and spans (T-09), rendering (Phase 4).

## Capabilities

### New Capabilities
- `agent-events`: the closed event set, the emitter interface and the sequence rules.

### Modified Capabilities
- None.

## Impact

- `src/Lunate.Agent` (new public types + `PublicAPI.Unshipped.txt`), `tests/Lunate.Agent.Tests`. No new packages.

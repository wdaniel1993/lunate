## Why

Layers 1 and 2 now have their pieces: the model pipeline (T-04 to T-06), the event contract (T-07) and the tool surface (T-08). Card T-09 makes them move: the agent loop — a turn loop on `IChatClient` that streams model output as events, executes tool calls, keeps the history valid, enforces the step limit, and produces one run span with nested model and tool spans. This is the architectural heart the rest of the product hangs off: every frontend (TUI, print mode, ACP) and every later card (sessions, compaction, approvals, MCP) builds on this loop, so its shape is fixed here, against the guide's turn sketch and ADR-0003.

## What Changes

- **`AgentHarness`** — `RunAsync(userInput, ct)` returns `IAsyncEnumerable<AgentEvent>`: one run id, events emitted through the channel (T-07) so tools share the same sink via `ToolContext.Events`, and the sequence obeys the event sequence rules.
- **The turn loop** — per the guide: append user message + `RunStarted`; request with system prompt (optional input; T-16 owns the real prompt), history and tool declarations; stream text events; complete calls via the pipeline's accumulator; execute tools; append assistant and tool messages; loop. `MaxSteps` default 50 → `StepLimitReached` + `RunFinished(step_limit)`; a plain answer ends with `RunFinished(stop)`.
- **Tool execution safety** — unknown tools, malformed arguments and tool exceptions become error results that tell the model what to do next, never exceptions out of `RunAsync`; tool output is truncated (T-08) before entering the history; a hard model-call failure ends the run with `RunError`.
- **Approval seam (minimal)** — an optional approver on the harness options (default: allow everything) so the loop's shape does not change when T-21 wires the interactive prompt; a decline becomes an error result.
- **Spans** — one `ActivitySource` ("Lunate.Agent"): a run span (`invoke_agent`) with child tool spans (`execute_tool`) carrying `gen_ai.tool.name`, `gen_ai.tool.call.id` and `lunate.tool.is_error`; model-call spans nest under the run span.
- **Acceptance** — three scripted sessions replayed end-to-end (text-only; one tool call; multi-step), byte-stable snapshots of the event sequences, and an in-memory-exporter assertion that a replayed session produces one run span with nested model and tool spans.
- Out of scope: error-path matrix, cancel and retries (T-10), session persistence (T-11), compaction (T-23), steering (T-22), the interactive approval prompt (T-21), MCP (T-29).

## Capabilities

### New Capabilities
- `agent-loop`: the harness surface, turn semantics, tool execution, spans and the run/event lifecycle.

### Modified Capabilities
- None.

## Impact

- `src/Lunate.Agent` (new public types + `PublicAPI.Unshipped.txt`), `tests/Lunate.Agent.Tests` (+ fixtures). No new packages.

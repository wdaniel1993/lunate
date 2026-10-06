# ADR-0013: The agent loop and harness surface

- Status: accepted — 2026-10-06 (maintainer sign-off)
- Date: 2026-10-06
- Relates to: ADR-0003 (own loop), ADR-0012 (tool contract)

## Context

ADR-0003 chose our own loop; T-04 to T-08 built the pipeline, the events and the tools. This change implements the loop and fixes its external shape — the contract every frontend and every later card builds on. The guide sketches the turn semantics; this ADR records the structural decisions around them.

## Decision

1. **One entry point**: `AgentHarness.RunAsync(userInput, ct) -> IAsyncEnumerable<AgentEvent>`. One run = one run id = one channel; the channel completes in a `finally`, and a hard failure ends the stream with `RunError`. Tools emit through the same sink (`ToolContext.Events`), so every event of a run travels one ordered path and the T-07 sequence rules hold for the whole stream.
2. **Turn semantics literally per the guide**: append user message + `RunStarted`; optional system prompt + history + tool declarations; stream text as events; complete calls via the pipeline accumulator; execute tools; append assistant and tool messages; repeat. `MaxSteps` (default 50) ends with `StepLimitReached` + `RunFinished(step_limit)`.
3. **Safety inside the loop**: unknown tools, malformed arguments and tool exceptions become instructing error results (ADR-0003); output is truncated before it enters the history; the UI-only `Details` never reach the model.
4. **An approval seam now, the prompt later**: `IToolApprover` on the harness options (default allow-all). A decline is an error result. The interactive prompt and `ApprovalRequested` emission are T-21; the loop's execution path does not change then.
5. **Spans**: `ActivitySource` "Lunate.Agent"; `invoke_agent` run span with `execute_tool` children; model-call spans nest under the run span. Attribute names follow the pinned OpenTelemetry GenAI conventions (Development status — verified at implementation).

## Consequences

- Frontends and later cards (T-10 to T-23, T-27, T-29) extend the loop without reshaping it.
- Run behavior is inspectable: the event sequence (validator) and the spans (exporter) are both testable in replay, without keys.
- The approver seam is dead code until T-21 by design; tests keep it honest.

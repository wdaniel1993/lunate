## Context

Card T-09; guide Layer 2 ("The loop, one turn", telemetry). ADR-0003 fixes that the loop is ours and that Microsoft.Extensions.AI never invokes tools. T-07's channel and validator and T-08's contract are the pieces this change composes. The loop is the contract every frontend and later card builds on — the design below is deliberately literal to the guide's sketch.

## Goals / Non-Goals

**Goals:** the exact turn semantics; one emission path (events channel) shared with tools; spans that make a run inspectable; deterministic replay acceptance; a loop shape that T-10 to T-23 extend without reshaping.

**Non-Goals:** the error-path matrix, cancellation and retries (T-10); sessions (T-11); compaction (T-23); steering (T-22); interactive approvals (T-21); MCP (T-29); the real system prompt (T-16).

## Decisions

- **Harness surface**: `AgentHarness(IChatClient client, ToolRegistry tools, AgentHarnessOptions? options = null)`; `RunAsync(string userInput, CancellationToken ct) -> IAsyncEnumerable<AgentEvent>`. One run = one run id (`run_<guid>`), one channel (T-07 `AgentEventChannel`), completed in a `finally` so a thrown loop cannot strand readers; `RunError` is emitted before completion on hard failures. The history is an in-memory `List<ChatMessage>` per harness (T-11 makes it durable); runs are sequential per harness (concurrent runs are a later, explicit decision).
- **Turn semantics exactly as the guide's sketch**: append user message + `RunStarted`; request = optional system prompt + history + `ChatOptions.Tools` from the registry declarations; stream; text events per assistant message with a generated message id; complete calls via the accumulator in the pipeline; no calls → append assistant message + `RunFinished(stop)`; calls → per call: `ToolCallStart` → `ToolCallArgs` (the complete arguments JSON — the accumulator guarantees completeness) → `ToolCallEnd` → execute → `ToolCallResult` → append tool message; repeat; `MaxSteps` (default 50) → `StepLimitReached` + `RunFinished(step_limit)`.
- **Tool execution**: `ToolContext(WorkingDirectory, Events)`; output truncated with `ToolOutput.Truncate` before it enters the history; the event's `Output` is what the model saw, `Details` stays UI-only; unknown tool, malformed JSON and exceptions become error results with instructing messages (ADR-0003) — the history stays valid and no exception leaves the stream except cancellation.
- **Approval seam (minimal; maintainer-approved)**: `AgentHarnessOptions.Approver` (`IToolApprover?`, default null = allow everything): `ValueTask<bool> ApproveAsync(ITool tool, JsonElement args, CancellationToken ct)`. A decline becomes an error result naming the decline. Emitting `ApprovalRequested` is T-21's job (the interactive flow); this change only fixes the seam so the loop shape is final. Alternative considered: defer the seam to T-21 — rejected because it would reshape the loop's execution path and its tests later.
- **Spans**: one `ActivitySource` ("Lunate.Agent") owned by the harness; run span `invoke_agent lunate` (attributes `gen_ai.operation.name=invoke_agent`, `gen_ai.agent.name=lunate`), tool child spans `execute_tool <tool>` (`gen_ai.operation.name=execute_tool`, `gen_ai.tool.name`, `gen_ai.tool.call.id`, `lunate.tool.is_error`). The gen_ai conventions are in Development status: verify the exact attribute names against the pinned spec at implementation and keep them in one constants file. Model-call spans from the factory's `UseOpenTelemetry` middleware nest under the run span via `Activity.Current` (the run span stays current across the streaming call). No listener → no measurable cost.
- **Determinism**: run ids and message ids are generated (not timestamps); snapshots assert event sequences with ids normalized. Tests use scripted providers (no network); the three acceptance sessions are recorded once via the scripted seam and replayed through the real pipeline (telemetry and accumulator active) like T-05's fixture tests.

## Risks / Trade-offs

- [Loop shape locks in early] → that is the point; the guide's sketch plus ADR-0003 already fixed the semantics, and T-10 to T-23 extend rather than reshape. Open questions below are surfaced for sign-off before apply.
- [Channel completion on failure] → try/finally, with a test that a throwing provider still completes the stream.
- [Span nesting depends on Activity.Current flowing through the async pipeline] → asserted by the in-memory-exporter acceptance test; if MEAI's middleware suppresses contexts, the fallback is an explicit parent (decide on evidence, note in the change).
- [Approver seam unused until T-21] → covered by tests now (decline path) so it cannot rot.
- [Scripted replay fixtures could drift from real provider streams] → the committed real fixtures (opencode-go, anthropic) stay for provider contract tests; loop tests script deterministically and the shapes are pinned by the accumulator contract.

## Migration Plan

Not applicable — additive.

## Open Questions

- None — the two sign-off questions were resolved by the maintainer: the approver seam is included (default allow-all; a decline becomes an error result), and the span agent name is `lunate`.

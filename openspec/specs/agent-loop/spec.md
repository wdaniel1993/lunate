# agent-loop Specification

## Purpose
The agent loop contract: one run as an event stream (`AgentHarness.RunAsync`), the turn semantics between model calls and tool execution, safety inside the loop (instructing error results, truncation, the approval seam), and the run and tool spans that make a run inspectable.

## Requirements

### Requirement: Harness surface
`Lunate.Agent` SHALL expose the loop as `AgentHarness.RunAsync(userInput, ct)` returning `IAsyncEnumerable<AgentEvent>`. One run SHALL carry exactly one run id, every event of the run SHALL travel the same ordered emission path (tools included, through `ToolContext.Events`), and the stream SHALL complete when the run ends — including when it ends in an error or in cancellation. Runs on one harness SHALL be sequential: a second concurrent run SHALL fail with an actionable error, and abandoning the stream SHALL stop the run.

#### Scenario: Events arrive in order and the sequence is valid
- **GIVEN** a scripted provider with a text-only answer
- **WHEN** a run executes
- **THEN** the consumer reads `RunStarted`, text events and exactly one terminal event, in order, and the sequence passes the event sequence rules

#### Scenario: A failing provider still completes the stream
- **GIVEN** a provider that throws during the model call
- **WHEN** the run executes
- **THEN** `RunError` is emitted and the stream completes

#### Scenario: Cancellation ends with a terminal event
- **GIVEN** a run in progress
- **WHEN** the caller cancels the run's token
- **THEN** the stream delivers `RunFinished(cancelled)` and completes

#### Scenario: Abandoning the stream stops the run
- **GIVEN** a consumer that stops reading mid-run
- **WHEN** the enumerator is disposed
- **THEN** the run stops and no further model or tool calls happen

#### Scenario: Overlapping runs are rejected
- **GIVEN** a run already in progress on a harness
- **WHEN** `RunAsync` is called again
- **THEN** it fails with an actionable error naming the sequential-runs rule

### Requirement: Turn semantics
A turn SHALL: append the user message and emit `RunStarted`; build the request from the optional system prompt, the history and the registry's tool declarations; stream text as events; treat function calls as complete (the factory pipeline's accumulator; the harness expects a pipeline-wrapped client); append the assistant message; with no calls, emit `RunFinished` with the mapped stop reason. With calls, each call SHALL emit `ToolCallStart`, `ToolCallArgs` (the complete arguments JSON, or the raw argument text when assembly failed), `ToolCallEnd`, then either execute the tool and emit `ToolCallResult` or, for arguments that cannot be assembled, emit an error `ToolCallResult` — appending the tool message in both cases — and the loop SHALL continue until a model answer has no calls or `MaxSteps` (default 50) is reached. Transient provider failures SHALL retry up to the configured limit with backoff, emitting `Retrying` per attempt, before the turn fails; only attempts that emitted no events retry. Provider usage reports SHALL emit `UsageUpdated`. When a session is attached to the harness options, every message the loop appends to the history SHALL also be appended to the session — the user message, each assistant message with model and usage when known, and each tool result including repaired synthetic ones; a resumed session SHALL seed the history before the run.

#### Scenario: One tool call then a final answer
- **GIVEN** a scripted provider that calls a tool once and then answers
- **WHEN** the run executes
- **THEN** the tool events bracket the execution, the tool message enters the history, and the run ends with `RunFinished(stop)`

#### Scenario: Step limit ends the run
- **GIVEN** a provider that keeps calling tools
- **WHEN** the run reaches `MaxSteps`
- **THEN** `StepLimitReached` is emitted, followed by `RunFinished(step_limit)`

#### Scenario: A transient failure retries and succeeds
- **GIVEN** a provider that fails transiently once and then answers
- **WHEN** the run executes
- **THEN** `Retrying` is emitted for the attempt and the run ends with `RunFinished(stop)`

#### Scenario: Exhausted retries fail the run
- **GIVEN** a provider that keeps failing transiently
- **WHEN** the retry limit is exhausted
- **THEN** `RunError` ends the run with the provider's message

#### Scenario: A sessioned run mirrors the conversation
- **GIVEN** a harness with an attached session
- **WHEN** a scripted run with a tool call executes
- **THEN** the session file contains the user, assistant and tool message entries in order with the parent chain, and a resumed session's first request contains the loaded history

### Requirement: Tool execution safety
Unknown tools, malformed arguments and exceptions from a tool SHALL become error results with instructing messages, never exceptions out of the stream. Arguments that cannot be assembled — a call still carrying raw `$arguments` fragments or a failed assembly, from the pipeline's accumulator or from a raw streaming client — SHALL never reach a tool: `ToolCallArgs` SHALL carry the raw argument text when it exists, falling back to the failure message otherwise, and the result SHALL instruct the model to fix the arguments. Tool output SHALL be truncated to the configured budget before it enters the history; `Details` SHALL remain UI-only; a decline from the approver SHALL become an error result, and an approver that fails SHALL produce an approval-specific error result, not one blaming the tool. The event sink passed to tools SHALL accept only extension events; anything else SHALL be rejected and surface as a tool error.

#### Scenario: Tool failure keeps the history valid
- **GIVEN** a scripted tool that throws
- **WHEN** the run executes
- **THEN** the model receives an error result naming what failed, and the run can continue

#### Scenario: Unassembled arguments never reach a tool
- **GIVEN** a call whose arguments are raw fragments or failed to assemble, from the pipeline's accumulator or from a raw streaming client
- **WHEN** the run executes
- **THEN** `ToolCallArgs` carries the raw argument text, no tool executes, and the model receives an instructing error result

#### Scenario: Long output is bounded
- **GIVEN** a tool whose output exceeds the budget
- **WHEN** its result enters the history
- **THEN** the model-facing output carries the truncation marker and the full output stays out of the model's history

#### Scenario: Approver failure is reported as an approval error
- **GIVEN** an approver that throws
- **WHEN** the tool call is considered
- **THEN** the error result names the approval failure, not a tool failure

#### Scenario: Tools cannot emit loop events
- **GIVEN** a tool that emits a non-extension event through its context
- **WHEN** it runs
- **THEN** the emission is rejected and the tool call becomes an error result

### Requirement: Run and tool spans
The loop SHALL emit one run span (`invoke_agent`) per run and one child span (`execute_tool`) per tool execution, carrying the pinned OpenTelemetry GenAI attributes plus `lunate.tool.is_error`; model-call spans SHALL nest under the run span. Without a listener, no spans SHALL be produced.

#### Scenario: A replayed session produces nested spans
- **GIVEN** an in-memory exporter and a replayed session with a tool call
- **WHEN** the run executes
- **THEN** one run span exists with a nested model span and a nested tool span carrying the tool name and call id

### Requirement: Cancellation and history repair
When a run is cancelled or fails after the assistant message was appended, the loop SHALL repair the history before the run ends: every function call in the last assistant message without a result SHALL get a synthetic error result — "cancelled by the user" on cancellation, a failure note on the error path — so the next run on the harness sends a valid history. A call already in flight SHALL also emit a synthetic `ToolCallResult` event so the event sequence stays valid. Cancellation SHALL end with `RunFinished(cancelled)` once `RunStarted` was emitted.

#### Scenario: A run cancelled mid-tool leaves a valid history
- **GIVEN** a run cancelled while a tool is executing
- **WHEN** the next run starts
- **THEN** the provider receives the assistant message followed by one synthetic cancelled result per dangling call

#### Scenario: A failure with pending calls repairs defensively
- **GIVEN** a failure that escapes while calls are still pending (an invariant break; tool failures normally become error results)
- **WHEN** the run ends with `RunError`
- **THEN** every pending call has a synthetic failure result and the next run sends a valid history

### Requirement: Nested calls in the loop
The loop SHALL execute nested tool calls (through `ToolContext.ExecuteToolAsync`) on the same ordered path as top-level calls: same approval, same cancellation token, same event stream. Nested results SHALL NOT enter the conversation history — only the outer call's result does. The depth cap (`MaxNestedToolDepth`) and the exposure gate SHALL be enforced by the loop. User cancellation SHALL propagate out of nested calls; every other failure SHALL become an error result.

#### Scenario: Nested work leaves history to the outer call
- **GIVEN** a run where a tool makes nested calls
- **WHEN** the run completes
- **THEN** the history contains the outer call's single result and no nested entries

#### Scenario: Cancellation propagates through nesting
- **GIVEN** a run cancelled while a nested call is in flight
- **WHEN** the cancellation reaches the nested call
- **THEN** the run stops with the cancellation semantics of T-10 and the history stays valid

### Requirement: Compaction
The loop SHALL compact the request before a model call when the estimated request size passes 80% of the model's context window — estimated as `chars / 4`, corrected by the last reported input-token usage when available — or when compaction is requested explicitly. Compaction SHALL summarize the history older than the kept tail (system prompt, AGENTS.md content, and the last N turns, N configurable, default 4) with the same model and a fixed summarization prompt covering goal, decisions, files touched and open problems. A tool call SHALL never be split from its result — the tail boundary SHALL expand to pair boundaries. The session file SHALL keep the full history; compaction SHALL be recorded as a `compaction` entry (`summary`, `replaces` = the replaced entry ids); in-run compaction SHALL emit `CompactionApplied` with the run id, replaced entry ids and the estimated tokens after compaction (an explicitly requested out-of-run compaction records the entry and rebuilds history — there is no run to carry an event). The rebuilt request SHALL be the system prompt plus the summary plus the kept tail. Compactor failure SHALL leave the request unchanged and SHALL be reported (the next request may retry). When the model window is unknown, a documented default SHALL apply and the run SHALL proceed.

#### Scenario: A long session compacts once
- **GIVEN** a recorded long session that passes the threshold
- **WHEN** the next model request is composed
- **THEN** it compacts exactly once, the next request is under 60% of the window, a `compaction` entry records the summary and replaced ids, `CompactionApplied` is emitted, and replay still matches

#### Scenario: Tool pairs are never split
- **GIVEN** a tail boundary falling between a tool call and its result
- **WHEN** compaction runs
- **THEN** the boundary expands so the pair stays together in the kept tail

#### Scenario: Compactor failure leaves the request unchanged
- **GIVEN** a summarization call that fails
- **WHEN** compaction is attempted
- **THEN** the request is sent unchanged and the failure is reported

#### Scenario: Resume after compaction
- **GIVEN** a session file containing a compaction entry
- **WHEN** the session is loaded and its history reconstructed
- **THEN** the history is the summary plus the messages after the replaced entries

### Requirement: System prompt and project instructions

The agent SHALL compose its system prompt from an embedded `system-prompt.md` template: identity, the available tools by name, and the working rules (read before edit, verify after change). The composer SHALL append the runtime facts (OS, resolved shell, canonical working directory, UTC date) and the contents of every `AGENTS.md` found from the repository root (worktree root when there is no repository) down to the working directory, in root-to-leaf order, each block introduced by its path. The composed prompt SHALL stay under 1,000 tokens (asserted with a documented heuristic), excluding user AGENTS.md content. The composed prompt SHALL be delivered through the harness system prompt so that it leads the model request as its system message.

#### Scenario: Composed prompt leads the request

- **GIVEN** a harness configured with the composed prompt and a fake model client
- **WHEN** a run starts
- **THEN** the request's first message is the composed system prompt

#### Scenario: AGENTS.md chain is appended root to leaf

- **GIVEN** a repository with `AGENTS.md` at the root and in a nested directory, running in that directory
- **WHEN** the prompt is composed
- **THEN** both files appear, root first, each under its relative path

#### Scenario: Budget holds with an empty chain

- **GIVEN** no `AGENTS.md` files
- **WHEN** the prompt is composed
- **THEN** it measures under 1,000 tokens by the documented estimator

### Requirement: Steering injection

The harness SHALL accept steering messages through a queue on its options while a run is active. The loop SHALL drain the queue before each model request, only after every tool result of the batch has been appended — never between a tool call and its result. Each drained message SHALL be appended to the history and, when a session is attached, to the session, then announced by a `SteeringInjected` event carrying the run id and the entry id (null without a session). Only the top-level run drains steering; leftover messages stay queued and the frontend may reclaim them.

#### Scenario: Steering lands after the batch, never inside it

- **GIVEN** a step with multiple tool calls and a queued steering message
- **WHEN** the loop processes the batch
- **THEN** every call/result pair is appended adjacently, and the steering message is appended after the batch's last result and before the next assistant message

#### Scenario: Leftover steering stays queued

- **GIVEN** a queued steering message that no model request follows
- **WHEN** the run ends
- **THEN** the message remains in the queue, reclaimable by the frontend, and no run starts automatically

#### Scenario: Nested calls never steer

- **GIVEN** a queued steering message and a nested tool call
- **WHEN** the nested call runs
- **THEN** the queue is not drained by it

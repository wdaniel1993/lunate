## MODIFIED Requirements

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
A turn SHALL: append the user message and emit `RunStarted`; build the request from the optional system prompt, the history and the registry's tool declarations; stream text as events; treat function calls as complete (the factory pipeline's accumulator; the harness expects a pipeline-wrapped client); append the assistant message; with no calls, emit `RunFinished` with the mapped stop reason. With calls, each call SHALL emit `ToolCallStart`, `ToolCallArgs` (the complete arguments JSON, or the raw argument text when assembly failed), `ToolCallEnd`, then either execute the tool and emit `ToolCallResult` or, for arguments that cannot be assembled, emit an error `ToolCallResult` — appending the tool message in both cases — and the loop SHALL continue until a model answer has no calls or `MaxSteps` (default 50) is reached. Transient provider failures SHALL retry up to the configured limit with backoff, emitting `Retrying` per attempt, before the turn fails; only attempts that emitted no events retry. Provider usage reports SHALL emit `UsageUpdated`.

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

## ADDED Requirements

### Requirement: Cancellation and history repair
When a run is cancelled or fails after the assistant message was appended, the loop SHALL repair the history before the run ends: every function call in the last assistant message without a result SHALL get a synthetic error result — "cancelled by the user" on cancellation, a failure note on the error path — so the next run on the harness sends a valid history. Cancellation SHALL end with `RunFinished(cancelled)` once `RunStarted` was emitted.

#### Scenario: A run cancelled mid-tool leaves a valid history
- **GIVEN** a run cancelled while a tool is executing
- **WHEN** the next run starts
- **THEN** the provider receives the assistant message followed by one synthetic cancelled result per dangling call

#### Scenario: A failure after calls repairs too
- **GIVEN** a run whose next model call fails after the assistant message was appended
- **WHEN** the run ends with `RunError`
- **THEN** every dangling call has a synthetic failure result and the next run sends a valid history

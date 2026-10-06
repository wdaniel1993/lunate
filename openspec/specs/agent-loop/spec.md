# agent-loop Specification

## Purpose
The agent loop contract: one run as an event stream (`AgentHarness.RunAsync`), the turn semantics between model calls and tool execution, safety inside the loop (instructing error results, truncation, the approval seam), and the run and tool spans that make a run inspectable.

## Requirements

### Requirement: Harness surface
`Lunate.Agent` SHALL expose the loop as `AgentHarness.RunAsync(userInput, ct)` returning `IAsyncEnumerable<AgentEvent>`. One run SHALL carry exactly one run id, every event of the run SHALL travel the same ordered emission path (tools included, through `ToolContext.Events`), and the stream SHALL complete when the run ends — including when it ends in an error.

#### Scenario: Events arrive in order and the sequence is valid
- **GIVEN** a scripted provider with a text-only answer
- **WHEN** a run executes
- **THEN** the consumer reads `RunStarted`, text events and exactly one terminal event, in order, and the sequence passes the event sequence rules

#### Scenario: A failing provider still completes the stream
- **GIVEN** a provider that throws during the model call
- **WHEN** the run executes
- **THEN** `RunError` is emitted and the stream completes

### Requirement: Turn semantics
A turn SHALL: append the user message and emit `RunStarted`; build the request from the optional system prompt, the history and the registry's tool declarations; stream text as events; treat function calls as complete (the factory pipeline's accumulator; the harness expects a pipeline-wrapped client); append the assistant message; with no calls, emit `RunFinished` with `stop`. With calls, each call SHALL emit `ToolCallStart`, `ToolCallArgs` (the complete arguments JSON, or the raw argument text when assembly failed), `ToolCallEnd`, then either execute the tool and emit `ToolCallResult` or, for arguments that cannot be assembled, emit an error `ToolCallResult` — appending the tool message in both cases — and the loop SHALL continue until a model answer has no calls or `MaxSteps` (default 50) is reached.

#### Scenario: One tool call then a final answer
- **GIVEN** a scripted provider that calls a tool once and then answers
- **WHEN** the run executes
- **THEN** the tool events bracket the execution, the tool message enters the history, and the run ends with `RunFinished(stop)`

#### Scenario: Step limit ends the run
- **GIVEN** a provider that keeps calling tools
- **WHEN** the run reaches `MaxSteps`
- **THEN** `StepLimitReached` is emitted, followed by `RunFinished(step_limit)`

### Requirement: Tool execution safety
Unknown tools, malformed arguments and exceptions from a tool SHALL become error results with instructing messages, never exceptions out of the stream. Arguments that cannot be assembled — a call still carrying raw `$arguments` fragments or a failed assembly, from the pipeline's accumulator or from a raw streaming client — SHALL never reach a tool: `ToolCallArgs` SHALL carry the raw argument text when it exists, falling back to the failure message otherwise, and the result SHALL instruct the model to fix the arguments. Tool output SHALL be truncated to the configured budget before it enters the history; `Details` SHALL remain UI-only; a decline from the approver SHALL become an error result.

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

### Requirement: Run and tool spans
The loop SHALL emit one run span (`invoke_agent`) per run and one child span (`execute_tool`) per tool execution, carrying the pinned OpenTelemetry GenAI attributes plus `lunate.tool.is_error`; model-call spans SHALL nest under the run span. Without a listener, no spans SHALL be produced.

#### Scenario: A replayed session produces nested spans
- **GIVEN** an in-memory exporter and a replayed session with a tool call
- **WHEN** the run executes
- **THEN** one run span exists with a nested model span and a nested tool span carrying the tool name and call id

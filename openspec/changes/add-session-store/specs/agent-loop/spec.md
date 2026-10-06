## MODIFIED Requirements

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

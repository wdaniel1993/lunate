# agent-events Specification

## Purpose
Define the observable contract between the agent loop and its consumers (TUI, recorders, tests): a closed, sealed event set aligned with AG-UI, a non-blocking emission interface, and sequence rules that make malformed streams detectable. The loop (T-09) emits these events, the TUI (T-10) renders them, and fixtures replay them deterministically.

## Requirements

### Requirement: Closed event set aligned with AG-UI

`Lunate.Agent` SHALL define `AgentEvent` as a closed, sealed set of records matching the guide's event table: `RunStarted`, `RunFinished(stopReason)`, `RunError`, `TextMessageStart`, `TextMessageContent`, `TextMessageEnd`, `ReasoningStart`, `ReasoningMessageContent`, `ReasoningEnd`, `ToolCallStart`, `ToolCallArgs`, `ToolCallEnd`, `ToolCallResult` (with UI-only `Details`), plus extension events (`ApprovalRequested`, `UsageUpdated`, `Retrying`, `CompactionApplied`, `StepLimitReached`, `ModelRouted`) deriving from a common extension base. Reasoning content SHALL be bracketed by `ReasoningStart`/`ReasoningEnd` per reasoning message, with `ReasoningMessageContent` carrying text fragments in arrival order and its own run-unique message id. Every event SHALL carry a run id. Child-run events forwarded onto a parent run's stream SHALL keep their own run id and session id and SHALL carry `ParentRunId` set to the parent run id; consumers of a stream SHALL scope events by run id and SHALL NOT assume a single run per stream. `RunFinished.StopReason` SHALL use the documented vocabulary: `stop` for a turn that finished normally, `cancelled` for a run cancelled through its token, `step_limit` for a run that reached `MaxSteps`, `length` for a turn the model's token limit cut off, and `token_limit` for a run that stopped because its configured token budget was exhausted before a model call; the loop maps the final turn's model finish reason accordingly (`length` → `length`, any other value → `stop`). The loop SHALL emit `Retrying` before each retry attempt and `UsageUpdated` when the provider reports usage; `UsageUpdated` SHALL carry the model id the usage belongs to (`ModelId` — the resolved backend for routed calls, the requested model otherwise), and a call routed through a virtual model SHALL emit `ModelRouted(requestedModel, resolvedModel)` when the resolution happens.

`UsageUpdated` SHALL carry the provider's prompt-cache counts when the provider reports them: cache reads in `UsageDetails.CachedInputTokenCount` and cache creations in `UsageDetails.AdditionalCounts["CacheCreationInputTokens"]`; a count the provider omits SHALL stay absent, never reported as zero.

#### Scenario: Set is closed and union-ready
- **GIVEN** the event types
- **WHEN** the public surface is inspected
- **THEN** every concrete event type is sealed and no event type is an interface

#### Scenario: Extension events are distinguishable
- **GIVEN** an extension event such as `ApprovalRequested`
- **WHEN** a consumer inspects it
- **THEN** it is identifiable as an extension without naming every extension type

#### Scenario: A length cut-off is reported as length
- **GIVEN** a final turn whose model finish reason is `length`
- **WHEN** the run ends
- **THEN** `RunFinished` carries the `length` stop reason

#### Scenario: Forwarded child events are attributable
- **GIVEN** a child run's events forwarded onto its parent's stream
- **WHEN** a consumer reads them
- **THEN** each carries the child's run id and session id with `ParentRunId` naming the parent run, so a run-scoped consumer can never confuse the two runs

#### Scenario: Routed calls are attributable
- **GIVEN** a call routed through a virtual model to a backend
- **WHEN** the call runs
- **THEN** `ModelRouted` names the requested and the resolved model ids, and the usage event for that call carries the resolved model id

#### Scenario: Reasoning streams are bracketed

- **GIVEN** a run whose model streams reasoning content
- **WHEN** the run's events are read
- **THEN** `ReasoningStart` opens once, `ReasoningMessageContent` fragments follow in order and `ReasoningEnd` closes the span

#### Scenario: Reasoning precedes text without interleaving

- **GIVEN** a model that streams a reasoning span and then a text message
- **WHEN** the run's events are read
- **THEN** the reasoning bracket closes before the text bracket opens, and no content event interleaves inside the other bracket

### Requirement: Event emission interface
`IAgentEvents` SHALL provide `Emit(AgentEvent)`. A channel-backed implementation SHALL expose emitted events as an `IAsyncEnumerable` for the harness boundary; tests SHALL use a recording implementation.

#### Scenario: Channel emission preserves order
- **GIVEN** events emitted through the channel implementation
- **WHEN** they are read as an async stream
- **THEN** they arrive in emission order and the stream completes when completed

### Requirement: Event sequence rules
The event stream SHALL obey: a run begins with exactly one `RunStarted`; exactly one terminal event (`RunFinished` or `RunError`) ends it — cancellation included, ending with `RunFinished(cancelled)`; text content events are bracketed by `TextMessageStart`/`TextMessageEnd` per message, including when the provider fails mid-message; reasoning content events are bracketed by `ReasoningStart`/`ReasoningEnd` per reasoning message, including when the provider fails mid-message; tool events follow start → args → end → result per call id; no event follows the terminal event; every event carries the run's id. Violations SHALL be detectable by a validator used in tests, including messages still open at the terminal event (text or reasoning), tool calls that never reached a result, and events with a foreign run id.

#### Scenario: Valid sequence passes
- **GIVEN** a well-formed run sequence
- **WHEN** it is validated
- **THEN** no violations are reported

#### Scenario: Broken sequences are rejected
- **GIVEN** a sequence with a missing start, an unbracketed message or an event after the terminal event
- **WHEN** it is validated
- **THEN** each violation is reported with the offending event

#### Scenario: Unclosed state at the terminal event is reported
- **GIVEN** a sequence whose terminal event arrives with an open text message, an open reasoning message, a tool call without a result, or an event from a different run
- **WHEN** it is validated
- **THEN** each violation is reported with the offending event

### Requirement: Event identity and source
Every `AgentEvent` SHALL carry optional `SessionId`, `ParentRunId` and `Source` (default `"core"`) as init properties on the base record, and the four tool events SHALL carry an optional `ParentToolCallId`. The event channel SHALL stamp `SessionId` on every event of a run that has a session attached. Nested call ids SHALL read `"<parent>/<n>"` with `n` a per-run counter, culture-invariant.

#### Scenario: A session-attached run stamps every event
- **GIVEN** a harness run with a session attached
- **WHEN** any event is emitted
- **THEN** it carries the session id; a detached run leaves it null

#### Scenario: Nested calls carry their parent id
- **GIVEN** a nested tool call executed through `ToolContext.ExecuteToolAsync`
- **WHEN** its tool events are emitted
- **THEN** they carry the nested call id (`<parent>/<n>`) and the parent call id

### Requirement: Tool progress events
A `ToolProgressUpdate` event SHALL carry run id, call id and message, and SHALL be emitted when a tool reports progress through `ToolContext.Progress`.

#### Scenario: A tool reporting progress emits the event
- **GIVEN** a tool that calls `ctx.Progress` with a message
- **WHEN** it runs
- **THEN** a `ToolProgressUpdate` with the current call id and the message is emitted

### Requirement: Unknown event kinds are ignored
Consumers of the event stream SHALL ignore event kinds they do not know (including extension events) without failing.

#### Scenario: An unknown kind passes through unharmed
- **GIVEN** an event of a kind a consumer does not handle
- **WHEN** the consumer processes the stream
- **THEN** it ignores the event and continues

### Requirement: SteeringInjected event

A steering message that the loop injects into the running history SHALL be announced as `SteeringInjected` on the run's event stream, carrying the run id and the id of the appended session entry (null when no session is attached). It SHALL travel the same ordered emission path as every other event of the run.

#### Scenario: One event per injected message, in order

- **GIVEN** a run with two queued steering messages
- **WHEN** the loop drains them before a model request
- **THEN** two `SteeringInjected` events are emitted in queue order, after the batch's tool events and before the next model-call events

# agent-events Specification

## MODIFIED Requirements

### Requirement: Closed event set aligned with AG-UI

`Lunate.Agent` SHALL define `AgentEvent` as a closed, sealed set of records matching the guide's event table: `RunStarted`, `RunFinished(stopReason)`, `RunError`, `TextMessageStart`, `TextMessageContent`, `TextMessageEnd`, `ReasoningStart`, `ReasoningMessageContent`, `ReasoningEnd`, `ToolCallStart`, `ToolCallArgs`, `ToolCallEnd`, `ToolCallResult` (with UI-only `Details`), plus extension events (`ApprovalRequested`, `UsageUpdated`, `Retrying`, `CompactionApplied`, `StepLimitReached`, `ModelRouted`) deriving from a common extension base. Reasoning content SHALL be bracketed by `ReasoningStart`/`ReasoningEnd` per reasoning message, with `ReasoningMessageContent` carrying text fragments in arrival order and its own run-unique message id. Every event SHALL carry a run id. Child-run events forwarded onto a parent run's stream SHALL keep their own run id and session id and SHALL carry `ParentRunId` set to the parent run id; consumers of a stream SHALL scope events by run id and SHALL NOT assume a single run per stream. `RunFinished.StopReason` SHALL use the documented vocabulary: `stop` for a turn that finished normally, `cancelled` for a run cancelled through its token, `step_limit` for a run that reached `MaxSteps`, `length` for a turn the model's token limit cut off, and `token_limit` for a run that stopped because its configured token budget was exhausted before a model call; the loop maps the final turn's model finish reason accordingly (`length` → `length`, any other value → `stop`). The loop SHALL emit `Retrying` before each retry attempt and `UsageUpdated` when the provider reports usage; `UsageUpdated` SHALL carry the model id the usage belongs to (`ModelId` — the resolved backend for routed calls, the requested model otherwise), and a call routed through a virtual model SHALL emit `ModelRouted(requestedModel, resolvedModel)` when the resolution happens.

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

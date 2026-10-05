## ADDED Requirements

### Requirement: Closed event set aligned with AG-UI
`Lunate.Agent` SHALL define `AgentEvent` as a closed, sealed set of records matching the guide's event table: `RunStarted`, `RunFinished(stopReason)`, `RunError`, `TextMessageStart`, `TextMessageContent`, `TextMessageEnd`, `ToolCallStart`, `ToolCallArgs`, `ToolCallEnd`, `ToolCallResult` (with UI-only `Details`), plus extension events (`ApprovalRequested`, `UsageUpdated`, `Retrying`, `CompactionApplied`, `StepLimitReached`) deriving from a common extension base. Every event SHALL carry a run id.

#### Scenario: Set is closed and union-ready
- **GIVEN** the event types
- **WHEN** the public surface is inspected
- **THEN** every concrete event type is sealed and no event type is an interface

#### Scenario: Extension events are distinguishable
- **GIVEN** an extension event such as `ApprovalRequested`
- **WHEN** a consumer inspects it
- **THEN** it is identifiable as an extension without naming every extension type

### Requirement: Event emission interface
`IAgentEvents` SHALL provide `Emit(AgentEvent)`. A channel-backed implementation SHALL expose emitted events as an `IAsyncEnumerable` for the harness boundary; tests SHALL use a recording implementation.

#### Scenario: Channel emission preserves order
- **GIVEN** events emitted through the channel implementation
- **WHEN** they are read as an async stream
- **THEN** they arrive in emission order and the stream completes when completed

### Requirement: Event sequence rules
The event stream SHALL obey: a run begins with `RunStarted`; exactly one terminal event (`RunFinished` or `RunError`) ends it; text content events are bracketed by `TextMessageStart`/`TextMessageEnd` per message; tool events follow start → args → end → result per call id; no event follows the terminal event. Violations SHALL be detectable by a validator used in tests.

#### Scenario: Valid sequence passes
- **GIVEN** a well-formed run sequence
- **WHEN** it is validated
- **THEN** no violations are reported

#### Scenario: Broken sequences are rejected
- **GIVEN** a sequence with a missing start, an unbracketed message or an event after the terminal event
- **WHEN** it is validated
- **THEN** each violation is reported with the offending event

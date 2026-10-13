## MODIFIED Requirements

### Requirement: Closed event set aligned with AG-UI

`Lunate.Agent` SHALL define `AgentEvent` as a closed, sealed set of records matching the guide's event table: `RunStarted`, `RunFinished(stopReason)`, `RunError`, `TextMessageStart`, `TextMessageContent`, `TextMessageEnd`, `ToolCallStart`, `ToolCallArgs`, `ToolCallEnd`, `ToolCallResult` (with UI-only `Details`), plus extension events (`ApprovalRequested`, `UsageUpdated`, `Retrying`, `CompactionApplied`, `StepLimitReached`) deriving from a common extension base. Every event SHALL carry a run id. Child-run events forwarded onto a parent run's stream SHALL keep their own run id and session id and SHALL carry `ParentRunId` set to the parent run id; consumers of a stream SHALL scope events by run id and SHALL NOT assume a single run per stream. `RunFinished.StopReason` SHALL use the documented vocabulary: `stop` for a turn that finished normally, `cancelled` for a run cancelled through its token, `step_limit` for a run that reached `MaxSteps`, `length` for a turn the model's token limit cut off, and `token_limit` for a run that stopped because its configured token budget was exhausted before a model call; the loop maps the final turn's model finish reason accordingly (`length` → `length`, any other value → `stop`). The loop SHALL emit `Retrying` before each retry attempt and `UsageUpdated` when the provider reports usage.

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

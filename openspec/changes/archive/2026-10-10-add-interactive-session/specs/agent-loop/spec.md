## ADDED Requirements

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

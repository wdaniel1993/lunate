## ADDED Requirements

### Requirement: SteeringInjected event

A steering message that the loop injects into the running history SHALL be announced as `SteeringInjected` on the run's event stream, carrying the run id and the id of the appended session entry (null when no session is attached). It SHALL travel the same ordered emission path as every other event of the run.

#### Scenario: One event per injected message, in order

- **GIVEN** a run with two queued steering messages
- **WHEN** the loop drains them before a model request
- **THEN** two `SteeringInjected` events are emitted in queue order, after the batch's tool events and before the next model-call events

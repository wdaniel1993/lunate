## ADDED Requirements

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

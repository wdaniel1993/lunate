## MODIFIED Requirements

### Requirement: Session store and resume

`Session` SHALL create a file (writing the header with the optional repository identity and worktree), append entries with store-assigned ids and parent chain (`AppendMessage`, `AppendCompaction`, `AppendModelChange`, `AppendActiveTools`, `AppendPromptSection`, `AppendChildSession`, `AppendNestedCalls`, `AppendExtension`), load a file validating the header schema — accepting schema 1 and 2, failing with an actionable error naming the schema and the file for anything newer — and reconstruct the message history for resume via `ToHistory()`. A child session started by the subagent runner SHALL be a session file beside its parent's (same sessions directory) with a fresh id, and the parent SHALL carry the `AppendChildSession(childSessionId, runId)` link before the child run starts; resuming the parent SHALL surface the link and resuming the child SHALL load the child's own history.

#### Scenario: Append and load round-trip
- **GIVEN** a session with appended messages
- **WHEN** it is loaded again
- **THEN** the entries and the reconstructed history match what was appended

#### Scenario: Schema mismatch fails actionably
- **GIVEN** a session file whose header schema is unknown
- **WHEN** it is loaded
- **THEN** loading fails with an error naming the schema and the file

#### Scenario: Resume reconstructs the conversation
- **GIVEN** a loaded session
- **WHEN** its history is reconstructed
- **THEN** the messages appear in append order, ready to seed a harness

#### Scenario: Child sessions link before the child runs
- **GIVEN** a subagent child run started from a parent with a session
- **WHEN** the child session file and the parent link are inspected
- **THEN** the child file sits beside the parent's with its own id, and the parent's child-session link (appended before the child started) names the child's session id and run id

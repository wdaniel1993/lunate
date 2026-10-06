# agent-sessions Specification

## Purpose
The session file: an append-only JSONL of conversation entries that makes runs durable and resumable, with a byte-for-byte format guard against Microsoft.Extensions.AI serialization drift.

## Requirements

### Requirement: Session file format
A session file SHALL be append-only JSONL: a header line (schema, session `id`, `cwd`, `created`, `meai` informational version) followed by one entry per line, each with `id`, `parentId`, `type` and a UTC timestamp. Message entries SHALL embed the `ChatMessage` exactly as `AIJsonUtilities` serializes it; assistant entries SHALL carry the model id and usage when known. Entry types SHALL cover messages, compaction (`summary`, `replaces`) and model changes (`model`).

#### Scenario: A golden session file round-trips byte for byte
- **GIVEN** a golden file under `tests/fixtures/sessions/`
- **WHEN** it is deserialized and re-serialized
- **THEN** the bytes are identical

#### Scenario: Entry chains are explicit
- **GIVEN** an appended entry
- **WHEN** it is serialized
- **THEN** it carries its own id, the previous entry's id as `parentId` and a UTC timestamp

### Requirement: Session store and resume
`Session` SHALL create a file (writing the header), append entries with store-assigned ids and parent chain, load a file validating the header schema with an actionable error naming the schema and the file, and reconstruct the message history for resume via `ToHistory()`.

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

### Requirement: Format guard
The golden files SHALL be verified byte for byte in the test suite; when a Microsoft.Extensions.AI update changes serialization, the schema SHALL be bumped and a migration added, recorded in an ADR — goldens SHALL NOT be edited silently to match new output.

#### Scenario: Serialization drift fails the guard
- **GIVEN** a Microsoft.Extensions.AI update that changes the embedded message output
- **WHEN** the golden test runs
- **THEN** it fails, pointing at the changed golden

### Requirement: Session location
`SessionPaths` SHALL place sessions under `~/.lunate/sessions/<project-hash>/`, one JSONL file per session named `<sessionId>.jsonl`, where the project hash is derived from the working directory.

#### Scenario: Location is stable per project
- **GIVEN** the same working directory
- **WHEN** the sessions directory is resolved twice
- **THEN** both resolutions are identical, and different directories hash differently

# agent-sessions Specification

## Purpose
The session file: an append-only JSONL of conversation entries that makes runs durable and resumable, with a byte-for-byte format guard against Microsoft.Extensions.AI serialization drift.

## Requirements

### Requirement: Session file format
A session file SHALL be append-only JSONL: a header line (schema, session `id`, `cwd`, `created`, `meai` informational version and — schema 2 — optional `repo` and `worktree` fields) followed by one entry per line, each with `id`, `parentId`, `type` and a UTC timestamp. Message entries SHALL embed the `ChatMessage` exactly as `AIJsonUtilities` serializes it; assistant entries SHALL carry the model id and usage when known; a loaded message whose content cannot be re-serialized byte-identically SHALL be preserved as its original raw JSON. Entry types SHALL cover messages, compaction (`summary`, `replaces`), model changes (`model`), active-tool changes (`tools`), prompt-section changes (`section`, `text`), child-session links (`childSessionId`, `runId`), bounded nested-call records (`callId` and `calls` — name, capped arguments, status and duration in milliseconds only, never results; at most 32 calls per entry, arguments truncated at 200 characters) and extension entries (see the extension-entry requirement). The current schema SHALL be 2; the loader SHALL accept schema 1 (v1 semantics: no `repo`/`worktree`, no schema-2 entry kinds) and schema 2.

#### Scenario: A golden session file round-trips byte for byte
- **GIVEN** a golden file under `tests/fixtures/sessions/`
- **WHEN** it is deserialized and re-serialized
- **THEN** the bytes are identical

#### Scenario: Entry chains are explicit
- **GIVEN** an appended entry
- **WHEN** it is serialized
- **THEN** it carries its own id, the previous entry's id as `parentId` and a UTC timestamp

#### Scenario: A schema 1 file loads unchanged
- **GIVEN** a schema 1 session file
- **WHEN** it is loaded
- **THEN** it loads with null `repo`/`worktree` and v1 entry semantics

#### Scenario: Nested-call records are bounded
- **GIVEN** a tool call with more than 32 nested calls or arguments beyond 200 characters
- **WHEN** the nested-call record is appended
- **THEN** arguments are truncated with a marker and at most 32 calls are recorded, never results

### Requirement: Session store and resume
`Session` SHALL create a file (writing the header with the optional repository identity and worktree), append entries with store-assigned ids and parent chain (`AppendMessage`, `AppendCompaction`, `AppendModelChange`, `AppendActiveTools`, `AppendPromptSection`, `AppendChildSession`, `AppendNestedCalls`, `AppendExtension`), load a file validating the header schema — accepting schema 1 and 2, failing with an actionable error naming the schema and the file for anything newer — and reconstruct the message history for resume via `ToHistory()`.

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
`SessionPaths` SHALL place sessions under `~/.lunate/sessions/`, one JSONL file per session named `<sessionId>.jsonl`. Inside a repository, sessions SHALL group by repository identity with one folder per worktree: `<hash8(repoIdentity)>/<hash8(worktreePath)>/`; outside a repository, sessions SHALL group by working directory: `<hash8(cwd)>/`. Each hash SHALL be the first eight lowercase hexadecimal characters of the SHA-256 of the respective string.

#### Scenario: Location is stable per project
- **GIVEN** the same working directory
- **WHEN** the sessions directory is resolved twice
- **THEN** both resolutions are identical, and different directories hash differently

#### Scenario: Grouping separates worktrees of one repository
- **GIVEN** two worktrees of one repository
- **WHEN** their session directories are resolved
- **THEN** both live under the same repository folder and each has its own worktree folder

### Requirement: Extension entries and unknown content
Extension entries SHALL use the namespaced type `ext/<extension-id>/<type>` with the standard envelope and an opaque JSON payload. The payload SHALL be preserved as raw JSON text and re-emitted byte for byte when the session is rewritten. Content the core can deserialize but not re-serialize byte-identically (for example provider-hosted tool results) SHALL be preserved in message entries the same way. Content with an unrecognized `$type` discriminator SHALL fail on load with an error naming the file and the line.

#### Scenario: Extension payloads survive a rewrite byte for byte

- **GIVEN** a session with an extension entry whose payload contains interior whitespace or ordering the writer chose
- **WHEN** it is loaded and re-serialized
- **THEN** the payload bytes are identical

#### Scenario: Unknown content survives a rewrite

- **GIVEN** a message entry containing a content kind the core can deserialize but cannot re-serialize byte-identically
- **WHEN** it is loaded and re-serialized
- **THEN** the original content bytes are preserved

#### Scenario: Unrecognized content fails actionably

- **GIVEN** a message entry with an unrecognized `$type` discriminator
- **WHEN** it is loaded
- **THEN** loading fails with an error naming the file and the line

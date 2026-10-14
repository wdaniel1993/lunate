## MODIFIED Requirements

### Requirement: Session file format

A session file SHALL be append-only JSONL: a header line (schema, session `id`, `cwd`, `created`, `meai` informational version and — schema 2 — optional `repo` and `worktree` fields) followed by one entry per line, each with `id`, `parentId`, `type` and a UTC timestamp. Message entries SHALL embed the `ChatMessage` exactly as `AIJsonUtilities` serializes it; assistant entries SHALL carry the model id and usage when known — the model id is the one actually used, so a call routed through a virtual or extension-registered model records the resolved backend; a loaded message whose content cannot be re-serialized byte-identically SHALL be preserved as its original raw JSON. Entry types SHALL cover messages, compaction (`summary`, `replaces`), model changes (`model`), active-tool changes (`tools`), prompt-section changes (`section`, `text`), child-session links (`childSessionId`, `runId`), bounded nested-call records (`callId` and `calls` — name, capped arguments, status and duration in milliseconds only, never results; at most 32 calls per entry, arguments truncated at 200 characters) and extension entries (see the extension-entry requirement). The current schema SHALL be 2; the loader SHALL accept schema 1 (v1 semantics: no `repo`/`worktree`, no schema-2 entry kinds) and schema 2.

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

#### Scenario: A routed response records its backend
- **GIVEN** a response produced through a virtual model
- **WHEN** the session entry is appended
- **THEN** it carries the resolved backend's model id, not the virtual model id

# Agent Sessions Specification Delta

## MODIFIED Requirements

### Requirement: Session file format

A session file SHALL be append-only JSONL: a header line (schema, session `id`, `cwd`, `created`, `meai` informational version and — schema 2 — optional `repo` and `worktree` fields; schema 3 adds optional `origin` (`{sessionId, entryId}`) and optional `branched: true`) followed by one entry per line, each with `id`, `parentId`, `type` and a UTC timestamp. Message entries SHALL embed the `ChatMessage` exactly as `AIJsonUtilities` serializes it; assistant entries SHALL carry the resolved model id and usage when known. Schema 4 assistant usage metadata SHALL identify the usage (`chat`, `fast`, `decision`, `subagents`, or `compaction`) and provider id; a decision usage SHALL additionally carry provider kind (`native` or `adapter`) and model id. A loaded message whose content cannot be re-serialized byte-identically SHALL be preserved as its original raw JSON. Entry types SHALL continue to cover messages, compaction (`summary`, `replaces`), model changes (`model`), active-tool changes (`tools`), prompt-section changes (`section`, `text`), child-session links (`childSessionId`, `runId`), bounded nested-call records (`callId` and `calls` — name, capped arguments, status and duration in milliseconds only, never results; at most 32 calls per entry, arguments truncated at 200 characters), extension entries, active-leaf moves (`activeLeaf`, `leafId`) and branch summaries (`branchSummary`, `fromLeafId`, `summary`). The active leaf SHALL be the most recently appended entry, unless a later `activeLeaf` entry points elsewhere. The current schema SHALL be 4; the loader SHALL accept schema 1 (v1 semantics: no `repo`/`worktree`, no schema-2 entry kinds), schema 2, schema 3, and schema 4. Schema 3 image parts SHALL remain serialized as `{"$type":"image","digest":"<sha256>","mime":"image/png|image/jpeg|image/gif|image/webp","width":<int>,"height":<int>}` with bytes in the image blob store. Schema 4 SHALL add usage attribution without changing the embedded `ChatMessage` representation. A reader SHALL preserve and round-trip schema 1–3 data unchanged and SHALL fail actionably on an unsupported future schema or unknown content discriminator. No schema SHALL contain a credential reference or secret.

#### Scenario: A golden session file round-trips byte for byte

- **GIVEN** an existing golden file under `tests/fixtures/sessions/`
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

#### Scenario: A schema 2 file loads unchanged

- **GIVEN** a schema 2 session file
- **WHEN** it is loaded and re-serialized
- **THEN** its bytes are unchanged

#### Scenario: A schema 3 file round-trips cache lanes

- **GIVEN** a schema 3 session whose usage carries positive cache-read and cache-write counts
- **WHEN** it is loaded and re-serialized
- **THEN** `input`, `output`, `cacheRead` and `cacheWrite` are byte-identical

#### Scenario: A zero cache count is omitted

- **GIVEN** a usage record whose provider reported no cache tokens
- **WHEN** the message entry is written
- **THEN** the usage object has no `cacheRead` or `cacheWrite` field

#### Scenario: A schema 3 image part round-trips

- **GIVEN** a schema 3 message carrying an image reference
- **WHEN** it is loaded and re-serialized
- **THEN** the `$type`, digest, mime, width and height are byte-identical

#### Scenario: Unknown parts still fail

- **GIVEN** a schema 4 message with an unrecognized `$type`
- **WHEN** it is loaded
- **THEN** loading fails naming the file and line

#### Scenario: Nested-call records are bounded

- **GIVEN** a tool call with more than 32 nested calls or arguments beyond 200 characters
- **WHEN** the nested-call record is appended
- **THEN** arguments are truncated with a marker and at most 32 calls are recorded, never results

#### Scenario: A routed response records its backend

- **GIVEN** a response produced through a virtual model
- **WHEN** the session entry is appended
- **THEN** it carries the resolved backend's model id, not the virtual model id

#### Scenario: Usage attribution round-trips in schema 4

- **GIVEN** schema 4 assistant usage for `fast` from provider `local-a` and model `local-a/model-x`
- **WHEN** the session is loaded and re-serialized
- **THEN** usage, provider, resolved model id, and token lanes are byte-identical

#### Scenario: Decision provenance distinguishes provider kinds

- **GIVEN** a schema 4 decision usage from an adapter model
- **WHEN** the decision record is written
- **THEN** it carries provider kind `adapter` and the resolved model id, with no credential reference or value

#### Scenario: Legacy sessions do not invent attribution

- **GIVEN** a schema 1, 2, or 3 session without usage/provider fields
- **WHEN** a schema 4 reader loads it
- **THEN** it loads with attribution absent rather than inferring a usage or provider

#### Scenario: Schema 4 golden is added without rewriting old fixtures

- **GIVEN** existing schema 1–3 golden fixtures and a new schema 4 fixture
- **WHEN** the format guard runs
- **THEN** every existing fixture remains byte-identical and the schema 4 fixture round-trips exactly

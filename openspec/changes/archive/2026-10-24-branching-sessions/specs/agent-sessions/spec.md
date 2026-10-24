## Purpose

Sessions become trees: an explicit active leaf, per-path compaction, branch summaries written where the model can see them, and fork/clone provenance — without rewriting the append-only file.

## MODIFIED Requirements

### Requirement: Session file format

A session file SHALL be append-only JSONL: a header line (schema, session `id`, `cwd`, `created`, `meai` informational version and — schema 2 — optional `repo` and `worktree` fields; schema 3 adds optional `origin` — `{sessionId, entryId}` naming the session and entry a `/fork` or `/clone` came from — and optional `branched: true`) followed by one entry per line, each with `id`, `parentId`, `type` and a UTC timestamp. Message entries SHALL embed the `ChatMessage` exactly as `AIJsonUtilities` serializes it; assistant entries SHALL carry the model id and usage when known — the model id is the one actually used, so a call routed through a virtual or extension-registered model records the resolved backend; a loaded message whose content cannot be re-serialized byte-identically SHALL be preserved as its original raw JSON. Entry types SHALL cover messages, compaction (`summary`, `replaces`), model changes (`model`), active-tool changes (`tools`), prompt-section changes (`section`, `text`), child-session links (`childSessionId`, `runId`), bounded nested-call records (`callId` and `calls` — name, capped arguments, status and duration in milliseconds only, never results; at most 32 calls per entry, arguments truncated at 200 characters) extension entries (see the extension-entry requirement), active-leaf moves (`activeLeaf`, `leafId`) and branch summaries (`branchSummary`, `fromLeafId`, `summary`). The active leaf SHALL be the most recently appended entry, unless a later `activeLeaf` entry points elsewhere. The current schema SHALL be 3; the loader SHALL accept schema 1 (v1 semantics: no `repo`/`worktree`, no schema-2 entry kinds), schema 2 and schema 3. Schema 3 adds one content kind: an image reference part serialized as `{"$type":"image","digest":"<sha256>","mime":"image/png|image/jpeg|image/gif|image/webp","width":<int>,"height":<int>}`; the bytes live in the image blob store, never in the message. A schema-2 reader SHALL continue to load schema 1 and 2 files unchanged; it rejects a schema-3 file at the header with the existing actionable schema error, because it cannot reach or skip a part it never parses. A schema-3 reader SHALL recognize the image part and the two tree kinds and SHALL still fail on an unrecognized `$type` discriminator. Messages, compactions, model changes and tool changes are the same kinds in schema 3 as in schema 2; only the image part and the tree kinds are new.

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
- **GIVEN** a schema 3 message with an unrecognized `$type`
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

## ADDED Requirements

### Requirement: Active path and per-path compaction

The model SHALL see exactly the entries on the active leaf's `parentId` chain, root to leaf. A compaction entry SHALL apply only when the compaction entry itself is on that chain; entry ids in its `replaces` list that are not on the chain SHALL be ignored rather than treated as missing. Request digests for a session with a single path SHALL be computed exactly as they are today, so the existing goldens pin the behaviour.

Switching the leaf SHALL append a `SessionActiveLeafEntry` naming the new leaf; the session's file SHALL NOT be rewritten. A leaf that points at an unknown entry id SHALL be refused at load with an actionable error naming the file and the id.

#### Scenario: Only the active path reaches the model

- **GIVEN** a session whose entries branch at one message
- **WHEN** the active leaf points down the second branch
- **THEN** the request carries only the entries on that branch's chain, root to leaf

#### Scenario: A compaction on a dead branch does not apply

- **GIVEN** a compaction entry on a branch that is not the active path
- **WHEN** the request is built
- **THEN** the compaction is ignored and the live entries on the active path are sent

#### Scenario: A single-path session keeps its digest

- **GIVEN** a session with no `activeLeaf` entry
- **WHEN** its request digest is computed
- **THEN** it equals the digest the current release computes for the same entries

#### Scenario: An unknown leaf id is refused

- **GIVEN** an `activeLeaf` entry naming an id the file does not contain
- **WHEN** the session is loaded
- **THEN** loading fails naming the file and the id

### Requirement: Branch summary on leave

When a branch switch is requested with the summary option enabled, the summariser SHALL make exactly one model call on the session's active model and SHALL write a `SessionBranchSummaryEntry` as the **first entry after the leaf being moved to**, with `parentId` equal to that leaf, `fromLeafId` naming the abandoned tip and `summary` the produced text. The entry SHALL NOT be a compaction and SHALL carry no `replaces` list. The prompt before confirming SHALL show the estimated input tokens and state that one model call will be made; the call's usage SHALL be recorded in the session's normal accounting. Without the option the switch SHALL write only the `activeLeaf` entry and SHALL NOT ask for confirmation: the abandoned branch stays reachable through `/tree`, and the notice after the move SHALL say so. The summary option's prompt SHALL name the model that will make the call and give an estimate of the call's cost.

#### Scenario: The summary sits on the path the model sees

- **GIVEN** a switch from branch A to branch B with the summary enabled
- **WHEN** the summary is written
- **THEN** its `parentId` is B's leaf, it is the next entry on B's path, and it is absent from A's path

#### Scenario: A summary is not a compaction

- **GIVEN** a written branch summary
- **WHEN** the request for the new path is built
- **THEN** the summary is ordinary context and no `replaces` list exists for it

#### Scenario: Disabled means no call

- **GIVEN** a switch with the summary option off
- **WHEN** it completes
- **THEN** no model call is made and only the `activeLeaf` entry is appended

### Requirement: Fork and clone provenance

`/fork` SHALL write a new session file containing the picked entry's own chain — root to that entry, whether or not it lies on the active path — with `schema: 3` and an `origin` header naming the source session and that entry. `/clone` SHALL write a new session file containing every entry of the source session, with the same `origin`, whose `entryId` SHALL be the source session's active leaf at the moment of cloning. Neither SHALL copy the follow-up queue, and a fork whose picked point is not a message boundary while a run is active SHALL be refused before any file is written.

#### Scenario: A fork starts at the picked entry

- **GIVEN** a session with ten entries
- **WHEN** `/fork` picks the fourth
- **THEN** the new file has a header carrying `origin` and exactly the entries up to and including the fourth

#### Scenario: A clone copies everything

- **GIVEN** a session with a branch that is not active
- **WHEN** `/clone` runs
- **THEN** the new file contains every entry of the source file and the same `origin`

#### Scenario: A fork while running is refused

- **GIVEN** an active run
- **WHEN** `/fork` runs
- **THEN** the command reports that the turn is still running and no file is created

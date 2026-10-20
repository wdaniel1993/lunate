## ADDED Requirements

### Requirement: Image blob store

A session SHALL store image bytes outside its JSONL, in a per-session directory beside the session file: `<sessions-dir>/<session-id>/images/<sha256>.<ext>` where `<ext>` is `png`, `jpg`, `gif` or `webp` from the sniffed media type and `<sha256>` is the lowercase hexadecimal SHA-256 of the bytes. Writes SHALL be content-addressed and deduplicated within the session: storing identical bytes twice SHALL leave one file. A write SHALL go to a temporary file in the same directory and then rename atomically; a failed write SHALL leave no partial blob. Only names matching `^[0-9a-f]{64}\.(png|jpg|gif|webp)$` SHALL be treated as blobs. A run without a session SHALL keep images in memory and write nothing.

A session open SHALL sweep its own image directory: after a successful load it SHALL collect every digest referenced by a message entry — typed image parts and raw-preserved message JSON alike — and delete only blob-named files whose digest is unreferenced, plus temporary files older than one day. The sweep SHALL never delete a referenced blob, never touch a directory outside the session's own image directory, and SHALL NOT run for a listing. Compaction SHALL NOT delete blobs: the full history keeps its entries even when the request no longer sends them.

The harness SHALL validate that every referenced blob exists before a resumed run starts and SHALL fail with an error naming the digest and the missing file; it SHALL NOT send a request with a silently missing image.

#### Scenario: Identical bytes are stored once

- **GIVEN** two attachments with identical PNG bytes in one session
- **WHEN** both are stored
- **THEN** the image directory contains one file named by the digest

#### Scenario: The sweep keeps referenced blobs

- **GIVEN** a loaded session whose entries reference digest A and an image directory holding A and B
- **WHEN** the session opens
- **THEN** A remains and B is removed

#### Scenario: The sweep understands raw-preserved messages

- **GIVEN** a message entry preserved as raw JSON that references digest A
- **WHEN** the session opens
- **THEN** A remains

#### Scenario: Compaction keeps the blobs

- **GIVEN** a session compacted so an old image turn is summarized
- **WHEN** the session is opened again
- **THEN** the blob referenced by the original message entry is still present

#### Scenario: A missing blob fails before the run

- **GIVEN** a session entry referencing a digest whose file was removed
- **WHEN** the run starts
- **THEN** it fails with an error naming the digest and the expected path, and no request is sent

#### Scenario: No-session writes nothing

- **GIVEN** a run started with `--no-session` and an image
- **WHEN** the run completes
- **THEN** the sessions directory contains no image directory

### Requirement: Listing never reads blobs

`Session.List` SHALL list sessions from their header lines only and SHALL NOT read, stat or sweep any image directory. An unreadable or very large image store SHALL NOT change the listing's ordering, bound or contents.

#### Scenario: Listing ignores the image store

- **GIVEN** a sessions directory holding a session with a large image directory
- **WHEN** the directory is listed
- **THEN** the listing returns the session and reads only its header line

## MODIFIED Requirements

### Requirement: Session file format

A session file SHALL be append-only JSONL: a header line (schema, session `id`, `cwd`, `created`, `meai` informational version and — schema 2 — optional `repo` and `worktree` fields) followed by one entry per line, each with `id`, `parentId`, `type` and a UTC timestamp. Message entries SHALL embed the `ChatMessage` exactly as `AIJsonUtilities` serializes it; assistant entries SHALL carry the model id and usage when known — the model id is the one actually used, so a call routed through a virtual or extension-registered model records the resolved backend; a loaded message whose content cannot be re-serialized byte-identically SHALL be preserved as its original raw JSON. Entry types SHALL cover messages, compaction (`summary`, `replaces`), model changes (`model`), active-tool changes (`tools`), prompt-section changes (`section`, `text`), child-session links (`childSessionId`, `runId`), bounded nested-call records (`callId` and `calls` — name, capped arguments, status and duration in milliseconds only, never results; at most 32 calls per entry, arguments truncated at 200 characters) and extension entries (see the extension-entry requirement). The current schema SHALL be 3; the loader SHALL accept schema 1 (v1 semantics: no `repo`/`worktree`, no schema-2 entry kinds), schema 2 and schema 3. Schema 3 adds one content kind: an image reference part serialized as `{"$type":"image","digest":"<sha256>","mime":"image/png|image/jpeg|image/gif|image/webp","width":<int>,"height":<int>}`; the bytes live in the image blob store, never in the message. A schema-2 reader SHALL continue to load schema 1 and 2 files unchanged; it rejects a schema-3 file at the header with the existing actionable schema error, because it cannot reach or skip a part it never parses. A schema-3 reader SHALL recognize the image part and SHALL still fail on an unrecognized `$type` discriminator.

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

### Requirement: Format guard
The golden files SHALL be verified byte for byte in the test suite; when a Microsoft.Extensions.AI update changes serialization, the schema SHALL be bumped and a migration added, recorded in an ADR — goldens SHALL NOT be edited silently to match new output. Schema 3 SHALL add a new image golden; every schema 1 and schema 2 golden SHALL keep its exact bytes as a load-compatibility golden.

#### Scenario: Serialization drift fails the guard
- **GIVEN** a Microsoft.Extensions.AI update that changes the embedded message output
- **WHEN** the golden test runs
- **THEN** it fails, pointing at the changed golden

#### Scenario: The schema 3 image golden is added

- **GIVEN** the schema 3 golden carrying an image reference
- **WHEN** the golden test runs
- **THEN** it round-trips byte for byte, and every schema 1 and schema 2 golden still does

# agent-sessions Specification

## Purpose
The session file: an append-only JSONL of conversation entries that makes runs durable and resumable, with a byte-for-byte format guard against Microsoft.Extensions.AI serialization drift.

## Requirements

### Requirement: Session file format

A session file SHALL be append-only JSONL: a header line (schema, session `id`, `cwd`, `created`, `meai` informational version and — schema 2 — optional `repo` and `worktree` fields; schema 3 adds optional `origin` (`{sessionId, entryId}`) and optional `branched: true`) followed by one entry per line, each with `id`, `parentId`, `type` and a UTC timestamp. Message entries SHALL embed the `ChatMessage` exactly as `AIJsonUtilities` serializes it; assistant entries SHALL carry the resolved model id and usage when known. Schema 4 assistant usage metadata SHALL identify the usage (`chat`, `fast`, `decision`, `subagents`, or `compaction`), profile name, provider name and provider kind, and resolved model id; a decision usage SHALL additionally carry decision method (`native` or `adapter`) and probability. A loaded message whose content cannot be re-serialized byte-identically SHALL be preserved as its original raw JSON. Entry types SHALL continue to cover messages, compaction (`summary`, `replaces`), model changes (`model`), active-tool changes (`tools`), prompt-section changes (`section`, `text`), child-session links (`childSessionId`, `runId`), bounded nested-call records (`callId` and `calls` — name, capped arguments, status and duration in milliseconds only, never results; at most 32 calls per entry, arguments truncated at 200 characters), extension entries, active-leaf moves (`activeLeaf`, `leafId`) and branch summaries (`branchSummary`, `fromLeafId`, `summary`). The active leaf SHALL be the most recently appended entry, unless a later `activeLeaf` entry points elsewhere. The current schema SHALL be 4; the loader SHALL accept schema 1 (v1 semantics: no `repo`/`worktree`, no schema-2 entry kinds), schema 2, schema 3, and schema 4. Schema 3 image parts SHALL remain serialized as `{"$type":"image","digest":"<sha256>","mime":"image/png|image/jpeg|image/gif|image/webp","width":<int>,"height":<int>}` with bytes in the image blob store. Schema 4 SHALL add usage attribution without changing the embedded `ChatMessage` representation. A reader SHALL preserve and round-trip schema 1–3 data unchanged; a reader that encounters a schema newer than it supports SHALL refuse at the header with “created by a newer Lunate, please update,” not a parse error or silent misread. Unknown content discriminators SHALL also fail actionably. No schema SHALL contain provider configuration, a credential reference or a secret.

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

Schema 4 usage attribution SHALL identify usage, profile name, provider name and provider kind, and resolved model id. Decision provenance SHALL additionally carry decision method (`native` or `adapter`) and probability. Session attribution SHALL NOT contain endpoints, headers, credential references, capabilities or secrets.

#### Scenario: Decision provenance records method and probability

- **GIVEN** a schema 4 decision usage from an adapter model
- **WHEN** the decision record is written
- **THEN** it carries decision method `adapter`, probability, profile name, provider name and kind, and resolved model id, with no provider configuration, credential reference or value

#### Scenario: Legacy sessions do not invent attribution

- **GIVEN** a schema 1, 2, or 3 session without usage/provider fields
- **WHEN** a schema 4 reader loads it
- **THEN** it loads with attribution absent rather than inferring a usage or provider

#### Scenario: Schema 4 golden is added without rewriting old fixtures

- **GIVEN** existing schema 1–3 golden fixtures and a new schema 4 fixture
- **WHEN** the format guard runs
- **THEN** every existing fixture remains byte-identical and the schema 4 fixture round-trips exactly

### Requirement: Session store and resume

`Session` SHALL create a file (writing the header with the optional repository identity and worktree), append entries with store-assigned ids and parent chain (`AppendMessage`, `AppendCompaction`, `AppendModelChange`, `AppendActiveTools`, `AppendPromptSection`, `AppendChildSession`, `AppendNestedCalls`, `AppendExtension`), load a file validating the header schema — accepting schema 1, 2 and 3, failing with an actionable error naming the schema and the file for anything newer — and reconstruct the message history for resume via `ToHistory()`. A child session started by the subagent runner SHALL be a session file beside its parent's (same sessions directory) with a fresh id, and its header SHALL record the repository identity and worktree of the workspace the child actually runs in — the child's own root when the spawn named one, the parent's fields otherwise — and the parent SHALL carry the `AppendChildSession(childSessionId, runId)` link before the child run starts; resuming the parent SHALL surface the link and resuming the child SHALL load the child's own history. A parent session with linked child sessions SHALL replay deterministically: repeated loads reconstruct the same messages and links, and the rendered link lines are stable.

The store SHALL also serve launch-time session selection. `--continue` SHALL resolve the most recently modified session whose header `cwd` equals the current working directory, compared with the platform's path comparison (ordinal on Linux, ordinal-ignore-case elsewhere) against the canonicalized working directory, walking the whole sessions root so a moved worktree still resolves; with no match it SHALL fail with a message naming the working directory. `--resume` SHALL open the interactive session picker and SHALL be refused in print mode with a message naming `--session` and `--continue`. `--session <id|path>` SHALL load that session — an id resolves to `<id>.jsonl` across the store, a value containing a path separator or the `.jsonl` suffix is a path — and a missing or ambiguous value SHALL fail naming it. `--no-session` SHALL keep the run in memory without writing a file. The selection flags SHALL be mutually exclusive with each other and with `--no-session`; the CLI SHALL report the conflict as a usage error naming both flags. In ACP mode the editor owns session creation and loading, so the selection flags SHALL be usage errors there.

A session MAY carry a title. `/name <title>` SHALL write it beside the session file in a sidecar named `<sessionId>.title` (one UTF-8 line, trimmed, interior whitespace collapsed, at most 120 characters), atomically and without touching the append-only JSONL; an empty or over-long title SHALL be refused with a notice. Bare `/name` SHALL report the title or `(none)`. The session picker SHALL label a titled session `<title> — <id>` and SHALL keep the listing's ordering, bound and corrupt-file handling unchanged; a missing sidecar means the session has no title, never an error.

`/session` SHALL report the current session without a model call: the file path, the id, the message count and the accumulated input/output/cache-read/cache-write tokens reconstructed from the session's recorded usages (so the totals survive a resume). It SHALL print a cost line only when every recorded message names a model the catalog knows and every positive token lane has a known price; otherwise it SHALL print tokens only and never a zero cost. A schema 1 or 2 session SHALL print no cost when its model has a cache rate, because its input count includes cache tokens whose split was not recorded. The cost line SHALL show the session total and a split by model and by token lane (input, output, cache read, cache write), formatted culture-invariantly.

`--system-prompt <text>` SHALL replace the composed system prompt wholesale for the run — including the template's tool-call conventions, which the caller then owns — and `--append-system-prompt <text>` SHALL append the text after the composed prompt as its own section. Both SHALL be recorded in the session as prompt sections (`system` and `system-append`), so resuming replays them; an explicit CLI directive SHALL win over the recorded one, and no directive SHALL leave composition unchanged. CLI values SHALL take precedence over the resolved settings (environment included), which take precedence over defaults, in every mode where the flag applies.

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

#### Scenario: Linked sessions replay deterministically
- **GIVEN** a parent session with child-session links and the linked child files
- **WHEN** the files are loaded and rendered repeatedly
- **THEN** the reconstructed messages, the links and the rendered link lines are identical every time

#### Scenario: A worktree child's header records its own worktree
- **GIVEN** a subagent spawn naming a workspace root in another worktree of the same repository
- **WHEN** the child session file is inspected
- **THEN** it sits beside the parent's and its header records the child root's repository identity and worktree path, distinct from the parent's worktree field

#### Scenario: Continue picks the newest session for the working directory

- **GIVEN** several sessions whose headers record the current working directory
- **WHEN** `lunate --continue` starts
- **THEN** the most recently modified of them loads and the conversation continues from its history

#### Scenario: Continue never crosses working directories

- **GIVEN** a newer session recorded for another working directory
- **WHEN** `--continue` resolves the current directory
- **THEN** that session is ignored, and with no session for the directory the command fails naming the directory

#### Scenario: Resume is refused in print mode

- **GIVEN** print mode
- **WHEN** `lunate -p --resume "hi"` runs
- **THEN** it exits non-zero without a model call, and the message names `--session` and `--continue`

#### Scenario: A specific session loads by id or path

- **GIVEN** a session id and a session file path
- **WHEN** `--session <id>` and `--session <path>` start
- **THEN** both load that file and its history seeds the run

#### Scenario: No-session writes nothing

- **GIVEN** an empty sessions directory
- **WHEN** a run starts with `--no-session`
- **THEN** the conversation works and the directory stays empty

#### Scenario: Selection flags conflict as a usage error

- **GIVEN** two selection flags, or a selection flag with `--no-session`
- **WHEN** the CLI parses the invocation
- **THEN** it exits with a usage error naming both flags and starts no session

#### Scenario: Name sets the title and the picker shows it

- **GIVEN** an idle session
- **WHEN** `/name Refactor the parser` is submitted and the session picker opens
- **THEN** the title is stored in the sidecar, the JSONL is unchanged, and the row reads `Refactor the parser — <id>`

#### Scenario: Session reports path, id, count and tokens without a price

- **GIVEN** a session whose model has no catalog pricing
- **WHEN** `/session` is submitted
- **THEN** it prints the path, id, message count and input/output tokens, and no cost line

#### Scenario: Session reports a priced total with splits

- **GIVEN** a session whose recorded messages all name priced models, with cache lanes
- **WHEN** `/session` is submitted
- **THEN** it prints the tokens, the total cost, the per-model split and the input/output/cache-read/cache-write split

#### Scenario: One unpriced model suppresses the total

- **GIVEN** a session with two models, one priced and one without pricing
- **WHEN** `/session` is submitted
- **THEN** it prints tokens only and no cost line, never `$0`

#### Scenario: An old session with cache pricing stays token-only

- **GIVEN** a schema 2 session whose model has a cache-read rate and no recorded cache counts
- **WHEN** `/session` is submitted
- **THEN** it prints tokens only, because the cache split is unknown

#### Scenario: Directives survive a resume

- **GIVEN** a session started with `--system-prompt` and `--append-system-prompt`
- **WHEN** it is resumed without those flags
- **THEN** the recorded sections compose the prompt identically, and an explicit CLI directive replaces the recorded one

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

### Requirement: Session listing

The store SHALL list a session directory for the picker: one entry per `.jsonl` file — session id, created (from the header) and last modified — ordered newest first by last-modified, then by creation time, then by session id, the creation time and id tie-breaks both descending, bounded to the most recent 20, skipping files whose header cannot be read. Listing SHALL NOT modify or rewrite any file.

#### Scenario: Newest first with a bound

- **GIVEN** more session files than the cap in a directory
- **WHEN** the directory is listed
- **THEN** at most 20 entries return, newest first, with unique ids

#### Scenario: Equal modification times order deterministically

- **GIVEN** session files sharing a modification time (for example two sessions created in the same second)
- **WHEN** the directory is listed
- **THEN** the order is deterministic: newest creation time first, then the id descending

#### Scenario: A corrupt file is skipped

- **GIVEN** a `.jsonl` whose header is unreadable
- **WHEN** the directory is listed
- **THEN** the file is skipped and the rest still list

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

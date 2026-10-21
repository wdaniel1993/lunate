## MODIFIED Requirements

### Requirement: Session file format

A session file SHALL be append-only JSONL: a header line (schema, session `id`, `cwd`, `created`, `meai` informational version and — schema 2 — optional `repo` and `worktree` fields) followed by one entry per line, each with `id`, `parentId`, `type` and a UTC timestamp. Message entries SHALL embed the `ChatMessage` exactly as `AIJsonUtilities` serializes it; assistant entries SHALL carry the model id and usage when known — the model id is the one actually used, so a call routed through a virtual or extension-registered model records the resolved backend; a loaded message whose content cannot be re-serialized byte-identically SHALL be preserved as its original raw JSON. Entry types SHALL cover messages, compaction (`summary`, `replaces`), model changes (`model`), active-tool changes (`tools`), prompt-section changes (`section`, `text`), child-session links (`childSessionId`, `runId`), bounded nested-call records (`callId` and `calls` — name, capped arguments, status and duration in milliseconds only, never results; at most 32 calls per entry, arguments truncated at 200 characters) and extension entries (see the extension-entry requirement). The current schema SHALL be 3; the loader SHALL accept schema 1 (v1 semantics: no `repo`/`worktree`, no schema-2 entry kinds), schema 2 and schema 3. A usage object SHALL optionally carry `cacheRead` and `cacheWrite` counts (schema 3), written only when the provider reported a positive count; a schema 1 or 2 usage object keeps exactly its `input`/`output` fields and bytes.

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
- **GIVEN** a schema 2 session file with a usage object carrying only input and output
- **WHEN** it is loaded and re-serialized
- **THEN** its bytes are identical

#### Scenario: A schema 3 file round-trips cache lanes
- **GIVEN** a schema 3 session whose usage carries positive cache-read and cache-write counts
- **WHEN** it is loaded and re-serialized
- **THEN** `input`, `output`, `cacheRead` and `cacheWrite` are byte-identical

#### Scenario: A zero cache count is omitted
- **GIVEN** a usage record whose provider reported no cache tokens
- **WHEN** the message entry is written
- **THEN** the usage object has no `cacheRead` or `cacheWrite` field

#### Scenario: Nested-call records are bounded
- **GIVEN** a tool call with more than 32 nested calls or arguments beyond 200 characters
- **WHEN** the nested-call record is appended
- **THEN** arguments are truncated with a marker and at most 32 calls are recorded, never results

#### Scenario: A routed response records its backend
- **GIVEN** a response produced through a virtual model
- **WHEN** the session entry is appended
- **THEN** it carries the resolved backend's model id, not the virtual model id

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
